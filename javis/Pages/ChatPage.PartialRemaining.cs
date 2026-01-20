using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Jarvis.Core.Archive;
using javis.Services;

namespace javis.Pages;

public partial class ChatPage : Page
{
    // This file contains remaining ChatPage members that have not yet been physically split.
    // Members are moved verbatim to keep behavior unchanged.

    private bool _soloStartQuestionsShown = false;

    private bool _soloKickoffPending = false;
    private int? _soloKickoffPick = null; // 1 or 2
    private string _soloKickoffQ1 = "";
    private string _soloKickoffQ2 = "";

    private static readonly string[] RandomStartQuestions =
    {
        "오늘 가장 에너지가 많이 드는 일은 뭐야? (왜 그런지도 같이)",
        "지금 가장 개선하고 싶은 습관 1개만 고른다면?",
        "이번 주에 반드시 끝내야 하는 1가지가 있다면?",
        "요즘 머릿속을 가장 많이 차지하는 고민은 뭐야?",
        "지금 당장 30분 안에 할 수 있는 가장 임팩트 큰 일은?",
        "최근에 배운 것 중에 가장 쓸모 있었던 건 뭐야?",
        "지금 프로젝트에서 가장 큰 리스크 1가지는 뭐라고 봐?",
        "오늘 기분을 0~10으로 점수 매기면? 그 이유는?"
    };

#if DEBUG
    private const bool FORCE_DEBATE_DEBUG = true;
#else
    private const bool FORCE_DEBATE_DEBUG = false;
#endif

    private CancellationTokenSource? _soloCts;
    private Task? _soloTask;

    private int _soloLastSeenMsgIndex = 0;
    private string? _soloLastNoteSig;

    private readonly Queue<string> _soloRecentTracks = new();
    private DateTimeOffset _soloLastNoteAt = DateTimeOffset.MinValue;
    private DateTimeOffset _soloLastMaintainAt = DateTimeOffset.MinValue;
    private DateTimeOffset _soloLastImproveAt = DateTimeOffset.MinValue;

    private int _soloGreetingStreak = 0;

    private int _soloCreateCount = 0;
    private DateTimeOffset _soloLastCreateAt = DateTimeOffset.MinValue;

    // FreePlay improve(�ڵ�ȭ) ����
    private DateTimeOffset _freePlayLastImproveAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan FreePlayImproveMinInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FreePlayMinInterval = TimeSpan.FromSeconds(45);

    // create_skill ee(ea f/ef)
    private static readonly TimeSpan CreateCooldown = TimeSpan.FromMinutes(30);
    private const int MAX_CREATES_PER_SESSION = 4;

    // SOLO loop protections
    private string? _soloLastReply;
    private int _soloSameReplyCount;
    private int _soloRepeatExitCount;

    private DateTimeOffset _debateLastAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan DebateMinInterval = TimeSpan.FromMinutes(2);
    private readonly Random _rng = new();

    private enum SoloTopicMode { FreePlay, FollowLastTopic }

    private SoloTopicMode _soloTopicMode = SoloTopicMode.FreePlay;
    private string _soloTopicSeed = "";
    private DateTimeOffset _soloTopicSeedAt = DateTimeOffset.MinValue;

    private void BeginSoloTopicMode()
    {
        var vm = (javis.ViewModels.ChatViewModel)DataContext;
        var seed = BuildLastTopicSeed(vm);

        if (string.IsNullOrWhiteSpace(seed))
        {
            _soloTopicMode = SoloTopicMode.FreePlay;
            _soloTopicSeed = "";
            _soloTopicSeedAt = DateTimeOffset.Now;
            vm.Messages.Add(new javis.Models.ChatMessage("assistant", "?? SOLO ����: ���� ���(��ȭ �����丮 ����)"));
        }
        else
        {
            _soloTopicMode = SoloTopicMode.FollowLastTopic;
            _soloTopicSeed = seed;
            _soloTopicSeedAt = DateTimeOffset.Now;
            vm.Messages.Add(new javis.Models.ChatMessage("assistant", "?? SOLO ����: �ֱ� ��ȭ�� �̾ �����Ұ�"));
        }
    }

    private string BuildLastTopicSeed(javis.ViewModels.ChatViewModel vm, int lastUserLines = 4, int maxChars = 700)
    {
        var userLines = vm.Messages
            .Where(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
            .Select(m => (m.Text ?? "").Trim())
            .Where(t => t.Length > 0)
            .TakeLast(lastUserLines)
            .ToList();

        if (userLines.Count == 0) return "";

        var seed = string.Join("\n", userLines);
        if (seed.Length > maxChars) seed = seed.Substring(seed.Length - maxChars);
        return seed;
    }

    private string BuildHistoryStartQuestion(javis.ViewModels.ChatViewModel vm)
    {
        var lastUser = vm.Messages
            .Where(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
            .Select(m => (m.Text ?? "").Trim())
            .Where(t => t.Length > 0)
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(lastUser))
            return "이전 대화가 거의 없어요. 지금 가장 해결하고 싶은 문제 1개만 말해줘.";

        lastUser = ChatTextUtil.TrimMax(lastUser, 80);
        return $"아까 \"{lastUser}\" 얘기했는데, 지금 가장 먼저 해결하고 싶은 포인트는 뭐야?";
    }

    private string PickRandomStartQuestion()
        => RandomStartQuestions[Random.Shared.Next(RandomStartQuestions.Length)];

    private void ShowSoloStartQuestions()
    {
        if (_soloStartQuestionsShown) return;
        _soloStartQuestionsShown = true;

        var vm = (javis.ViewModels.ChatViewModel)DataContext;

        var q1 = BuildHistoryStartQuestion(vm);
        var q2 = PickRandomStartQuestion();

        _soloKickoffPending = true;
        _soloKickoffPick = null;
        _soloKickoffQ1 = q1;
        _soloKickoffQ2 = q2;

        if (vm.SelectedRoom == javis.ViewModels.ChatRoom.Duo)
        {
            vm.Messages.Add(new javis.Models.ChatMessage("assistant_a", $"1) {q1}"));
            vm.Messages.Add(new javis.Models.ChatMessage("assistant_b", $"2) {q2}"));
            vm.Messages.Add(new javis.Models.ChatMessage("assistant", "원하는 질문(1 또는 2)에 답해줘."));
        }
        else
        {
            vm.Messages.Add(new javis.Models.ChatMessage(
                "assistant",
                $"시작할게.\n\n1) {q1}\n\n2) {q2}\n\n원하는 질문에 답해줘."));
        }
    }

    private (bool idle, string newUserText, string recentContext, string topicMode, string topicSeed)
        SnapshotSoloInputs(int maxRecentChars = 1400)
    {
        var vm = (javis.ViewModels.ChatViewModel)DataContext;

        var total = vm.Messages.Count;
        var delta = vm.Messages.Skip(_soloLastSeenMsgIndex).ToList();
        _soloLastSeenMsgIndex = total;

        var newUserText = string.Join("\n",
            delta.Where(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                 .Select(m => (m.Text ?? "").Trim())
                 .Where(t => t.Length > 0 && !IsFakeUserText(t)));

        var idle = string.IsNullOrWhiteSpace(newUserText);

        var recent = vm.Messages
            .Where(m =>
            {
                var role = (m.Role ?? "").ToLowerInvariant();
                var text = (m.Text ?? "").Trim();
                if (text.Length == 0) return false;
                if (role == "user") return true;
                if (role.StartsWith("assistant")) return !IsSoloMetaText(text);
                return false;
            })
            .TakeLast(10)
            .Select(m => $"{m.Role}: {m.Text}");

        var recentContext = string.Join("\n", recent);
        if (recentContext.Length > maxRecentChars)
            recentContext = recentContext.Substring(recentContext.Length - maxRecentChars);

        if (!idle)
        {
            _soloTopicMode = SoloTopicMode.FollowLastTopic;
            _soloTopicSeed = BuildLastTopicSeed(vm);
            _soloTopicSeedAt = DateTimeOffset.Now;
        }

        return (idle,
                newUserText,
                recentContext,
                _soloTopicMode.ToString(),
                _soloTopicSeed);
    }

    private static bool IsSoloMetaText(string text)
    {
        if (text.Contains("SOLO 36", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("34f0 47", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("45", StringComparison.OrdinalIgnoreCase) && text.Contains("SOLO", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith("?? [SOLO", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith("???", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("user_activation", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("solo_mode", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private static bool IsFakeUserText(string? t)
    {
        t = (t ?? "").Trim();
        if (t.Length == 0) return true;

        if (t.StartsWith("SOLO ", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.StartsWith("(debug)", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.StartsWith("/debate", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.Contains("원하는 질문", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private void MaybeExpireTopic()
    {
        if (_soloTopicMode != SoloTopicMode.FollowLastTopic) return;
        if (DateTimeOffset.Now - _soloTopicSeedAt < TopicTtl) return;

        _soloTopicMode = SoloTopicMode.FreePlay;
        _soloTopicSeed = "";
        _soloTopicSeedAt = DateTimeOffset.Now;

        ((javis.ViewModels.ChatViewModel)DataContext).Messages.Add(
            new javis.Models.ChatMessage("assistant", "?? ������ �����Ǿ� ���� ���� ��ȯ�Ұ�."));
    }

    private bool IsRepeatNote(string title, string body)
    {
        var sig = (title + "|" + body).Trim();
        if (sig == _soloLastNoteSig)
        {
            _soloRepeatHit++;
            return true;
        }
        _soloLastNoteSig = sig;
        _soloRepeatHit = 0;
        return false;
    }

    private static readonly TimeSpan TopicTtl = TimeSpan.FromMinutes(35);

    private int _soloRepeatHit = 0;
    private int _soloAngle = 0;

    private int GetAngleIdAndAdvance()
    {
        var id = _soloAngle % 5;
        _soloAngle++;
        return id;
    }

    private static string AngleInstruction(int angleId)
        => angleId switch
        {
            0 => "����/���: ���� ������ 3~6�������� ����",
            1 => "�ݷ�/����ũ: Ʋ�� �� �ִ� ����, ������, �ݴ�ٰ�",
            2 => "���� �ൿ: ���� �� �� �ִ� 3���� �׼�(��ü��)",
            3 => "�ڵ�ȭ ����: �ݺ�/�ڵ�ȭ ��ġ�� �ִ� �κ�(��ų �ĺ� ����)",
            4 => "���� ����: ���� �̾ ���� 2~5��",
            _ => "����/���"
        };

    private readonly SemaphoreSlim _soloGenLock = new(1, 1);
    private DateTimeOffset _soloLastGenStartedAt = DateTimeOffset.MinValue;

    private async Task<bool> TryRunSoloGenerationAsync(Func<CancellationToken, Task> work, CancellationToken ct)
    {
        if (!await _soloGenLock.WaitAsync(0, ct))
            return false;

        try
        {
            _soloLastGenStartedAt = DateTimeOffset.Now;
            await work(ct);
            return true;
        }
        finally
        {
            _soloGenLock.Release();
        }
    }

    private static async Task RunWithTimeoutAsync(Func<CancellationToken, Task> work, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        await work(cts.Token);
    }

    // NOTE: BuildRecentContext moved to ChatPage.Solo.cs
    // NOTE: BuildSoloPrompt moved to ChatPage.Solo.cs

    // NOTE: SoloProcessOneTurnAsync moved to ChatPage.Solo.cs

    // NOTE: PickNextTrack moved to ChatPage.Solo.cs
    // NOTE: MarkTrack moved to ChatPage.Solo.cs

    private OllamaClient LlmA { get; } = new("http://localhost:11434", "qwen3:4b");
    private OllamaClient LlmB { get; } = new("http://localhost:11434", "qwen3:8b");
    private OllamaClient Llm { get; } = new("http://localhost:11434", "qwen3:4b");
}
