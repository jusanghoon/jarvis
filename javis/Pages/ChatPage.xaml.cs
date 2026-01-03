using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using javis.Services;
using javis.ViewModels;

namespace javis.Pages;

public partial class ChatPage : Page
{
    private readonly ChatViewModel _vm = new();

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

    // FreePlay improve(자동화) 제어
    private DateTimeOffset _freePlayLastImproveAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan FreePlayImproveMinInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FreePlayMinInterval = TimeSpan.FromSeconds(45);

    // create_skill 제한(세션/쿨다운)
    private static readonly TimeSpan CreateCooldown = TimeSpan.FromMinutes(30);
    private const int MAX_CREATES_PER_SESSION = 4;

    private PluginHost Host => PluginHost.Instance;
    private OllamaClient Llm { get; } = new("http://localhost:11434", "qwen3:4b");

    private void AppendAssistant(string text)
    {
        ChatBus.Send(text);
    }

    private void SetSoloStatus(string text)
        => _ = UiAsync(() =>
        {
            if (SoloStatusText == null) return;
            SoloStatusText.Text = text ?? "";
        });

    private void AddJarvisMessage(string text)
    {
        ChatBus.Send(text);
    }

    public ChatPage()
    {
        InitializeComponent();
        DataContext = _vm;

        _vm.ScrollToEndRequested += ScrollToEnd;

        Loaded += ChatPage_Loaded;
        Unloaded += ChatPage_Unloaded;
    }

    private void ChatPage_Loaded(object sender, RoutedEventArgs e)
    {
        InputBox.Focus();

        ChatBus.MessageQueued += OnBusMessage;

        while (ChatBus.TryDequeue(out var t))
            _ = _vm.SendExternalAsync(t);
    }

    private void ChatPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ChatBus.MessageQueued -= OnBusMessage;
    }

    private void OnBusMessage(string text)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            await _vm.SendExternalAsync(text);
        });
    }

    private enum SoloTopicMode { FreePlay, FollowLastTopic }

    private SoloTopicMode _soloTopicMode = SoloTopicMode.FreePlay;
    private string _soloTopicSeed = "";
    private DateTimeOffset _soloTopicSeedAt = DateTimeOffset.MinValue;

    private void BeginSoloTopicMode()
    {
        var vm = (ChatViewModel)DataContext;
        var seed = BuildLastTopicSeed(vm);

        if (string.IsNullOrWhiteSpace(seed))
        {
            _soloTopicMode = SoloTopicMode.FreePlay;
            _soloTopicSeed = "";
            _soloTopicSeedAt = DateTimeOffset.Now;
            vm.Messages.Add(new javis.Models.ChatMessage("assistant", "? SOLO 시작: 자유 모드(대화 주제 없음)"));
        }
        else
        {
            _soloTopicMode = SoloTopicMode.FollowLastTopic;
            _soloTopicSeed = seed;
            _soloTopicSeedAt = DateTimeOffset.Now;
            vm.Messages.Add(new javis.Models.ChatMessage("assistant", "? SOLO 시작: 마지막 주제 이어서 생각할게"));
        }
    }

    private string BuildLastTopicSeed(ChatViewModel vm, int lastUserLines = 4, int maxChars = 700)
    {
        // 최근 user 발화 몇 개를 seed로 사용 (assistant는 넣지 않음: 에코/메타 반복 방지)
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

    private void SoloToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_soloCts != null) return;

        BeginSoloTopicMode();

        var vm = (ChatViewModel)DataContext;
        vm.ContextVars["solo_mode"] = "on";
        vm.ContextVars["user_action"] = "solo_start";

        _soloCts = new CancellationTokenSource();

        if (SoloStatusText != null)
            SoloStatusText.Visibility = Visibility.Visible;

        SetSoloStatus("SOLO \uC2DC\uC791\u2026");

        AppendAssistant("SOLO \uBAA8\uB4DC ON (\uB2E4\uC2DC \uB20C\uB7EC\uC11C \uC885\uB8CC)");

        try { javis.App.Kernel?.Logger?.Log("solo.start", new { }); } catch { }

        _soloTask = Task.Run(() => SoloLoopAsync(_soloCts.Token));
    }

    private async void SoloToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_soloCts == null) return;

        var vm = (ChatViewModel)DataContext;
        vm.ContextVars["solo_mode"] = "off";
        vm.ContextVars["user_action"] = "solo_stop";

        SetSoloStatus("\uC885\uB8CC \uC694\uCCAD\u2026");
        AppendAssistant("SOLO \uBAA8\uB4DC OFF \uC694\uCCAD\u2026");

        try { javis.App.Kernel?.Logger?.Log("solo.stop_request", new { }); } catch { }

        _soloCts.Cancel();

        try { if (_soloTask != null) await _soloTask; } catch { }

        _soloCts.Dispose();
        _soloCts = null;
        _soloTask = null;

        SetSoloStatus(string.Empty);
        if (SoloStatusText != null)
            SoloStatusText.Visibility = Visibility.Collapsed;

        AppendAssistant("SOLO \uBAA8\uB4DC \uC885\uB8CC.");

        try { javis.App.Kernel?.Logger?.Log("solo.stopped", new { }); } catch { }
    }

    private (bool idle, string newUserText, string recentContext, string topicMode, string topicSeed)
        SnapshotSoloInputs(int maxRecentChars = 1400)
    {
        var vm = (ChatViewModel)DataContext;

        // 새 user 입력(증분)
        var total = vm.Messages.Count;
        var delta = vm.Messages.Skip(_soloLastSeenMsgIndex).ToList();
        _soloLastSeenMsgIndex = total;

        var newUserText = string.Join("\n",
            delta.Where(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                 .Select(m => (m.Text ?? "").Trim())
                 .Where(t => t.Length > 0));

        var idle = string.IsNullOrWhiteSpace(newUserText);

        // recentContext (SOLO 메타 제외)
        var recent = vm.Messages
            .Where(m =>
            {
                var role = (m.Role ?? "").ToLowerInvariant();
                var text = (m.Text ?? "").Trim();
                if (text.Length == 0) return false;
                if (role == "user") return true;
                if (role == "assistant") return !IsSoloMetaText(text);
                return false;
            })
            .TakeLast(10)
            .Select(m => $"{m.Role}: {m.Text}");

        var recentContext = string.Join("\n", recent);
        if (recentContext.Length > maxRecentChars)
            recentContext = recentContext.Substring(recentContext.Length - maxRecentChars);

        // ? 사용자가 SOLO 중 새로 말하면, 그걸 "새 주제"로 업데이트
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
        // ? 여기 패턴이 "계속 종료 강요" + "SOLO 활성화" 반복의 근원이라 강하게 컷
        if (text.Contains("SOLO 모드", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("다시 눌러", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("종료", StringComparison.OrdinalIgnoreCase) && text.Contains("SOLO", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith("?? [SOLO", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.StartsWith("???", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("user_activation", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("solo_mode", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private string PickNextTrack(bool idle)
    {
        bool UsedRecently(string t) => _soloRecentTracks.Contains(t);

        var candidates = idle
            ? new[] { "reflect", "maintain", "rest", "improve" }
            : new[] { "converse", "reflect", "maintain", "rest" };

        var now = DateTimeOffset.Now;
        bool okReflect = now - _soloLastNoteAt > TimeSpan.FromSeconds(20);
        bool okMaintain = now - _soloLastMaintainAt > TimeSpan.FromMinutes(2);
        bool okImprove = now - _soloLastImproveAt > TimeSpan.FromMinutes(10);

        foreach (var t in candidates)
        {
            if (UsedRecently(t)) continue;
            if (t == "reflect" && !okReflect) continue;
            if (t == "maintain" && !okMaintain) continue;
            if (t == "improve" && !okImprove) continue;
            return t;
        }

        return "rest";
    }

    private void MarkTrack(string track)
    {
        _soloRecentTracks.Enqueue(track);
        while (_soloRecentTracks.Count > 6) _soloRecentTracks.Dequeue();

        var now = DateTimeOffset.Now;
        if (track == "reflect") _soloLastNoteAt = now;
        if (track == "maintain") _soloLastMaintainAt = now;
        if (track == "improve") _soloLastImproveAt = now;
    }

    private static readonly TimeSpan TopicTtl = TimeSpan.FromMinutes(35);

    private int _soloRepeatHit = 0;
    private int _soloAngle = 0;

    private void MaybeExpireTopic()
    {
        if (_soloTopicMode != SoloTopicMode.FollowLastTopic) return;
        if (DateTimeOffset.Now - _soloTopicSeedAt < TopicTtl) return;

        _soloTopicMode = SoloTopicMode.FreePlay;
        _soloTopicSeed = "";
        _soloTopicSeedAt = DateTimeOffset.Now;

        ((ChatViewModel)DataContext).Messages.Add(
            new javis.Models.ChatMessage("assistant", "?? 주제가 오래되어 자유 모드로 전환할게."));
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

    private int GetAngleIdAndAdvance()
    {
        var id = _soloAngle % 5;
        _soloAngle++;
        return id;
    }

    private static string AngleInstruction(int angleId)
        => angleId switch
        {
            0 => "관찰/요약: 지금 주제를 3~6문장으로 정리",
            1 => "반례/리스크: 틀릴 수 있는 가정, 위험요소, 반대근거",
            2 => "다음 행동: 당장 할 수 있는 3가지 액션(구체적)",
            3 => "자동화 관점: 반복/자동화 가치가 있는 부분(스킬 후보 포함)",
            4 => "질문 생성: 내일 이어갈 질문 2~5개",
            _ => "관찰/요약"
        };

    private string BuildSoloPrompt(
        bool idle,
        string track,
        string newUserText,
        string recentContext,
        string skillSummaries,
        string vaultSnippets,
        string repeatCandidateBlock,
        string topicMode,
        string topicSeed)
    {
        var hasTopic = !string.IsNullOrWhiteSpace(topicSeed);
        var hasSnippets = !(vaultSnippets?.Contains("없음") ?? true) && !(vaultSnippets?.Contains("없습니다") ?? true);

        var canonBlock = Host.Canon.BuildPromptBlock(query: (newUserText + " " + vaultSnippets + " " + topicSeed).Trim(), maxItems: 6);

        var angleId = GetAngleIdAndAdvance();
        var angleText = AngleInstruction(angleId);

        return $$"""
{Host.Persona.CoreText}

{Host.Persona.SoloOverlayText}

[CANON]
{canonBlock}

idle={idle}
track={track}
topic_mode={topicMode}
has_topic={hasTopic}
has_snippets={hasSnippets}

angle_id={angleId}
angle_rule={angleText}

[TOPIC SEED]
{(hasTopic ? topicSeed : "(없음)")}

[최근 대화(참고)]
{recentContext}

[새 사용자 입력(있으면 이것만 반응)]
{newUserText}

[반복 감지 후보]
{repeatCandidateBlock}

[현재 스킬]
{skillSummaries}

[최근 자료 스니펫]
{vaultSnippets}

핵심 규칙:
- "SOLO", "활성화", "종료", "다시 눌러" 같은 메타 문장/안내 문장 금지.
- 이번 턴은 angle_rule을 반드시 따른다.

- topic_mode=FollowLastTopic이면:
  - idle=true여도 TOPIC SEED를 중심으로 혼자 생각을 계속한다.
  - 혼자 토론(자문자답/찬반)은 say로 길게 떠들지 말고 save_note로 남긴다.
- topic_mode=FreePlay이면:
  - 외부 재료 없어도 일반지식 기반으로 “혼자 놀기”를 한다(아이디어, 설계, 점검, 백로그).
- create_skill은 다음 조건에서만:
  - 반복 감지 후보가 있거나
  - TOPIC SEED에 대해 “반복적으로 자동화 가치가 명확”할 때
- has_snippets=false이고 has_topic=false이면: sleep 또는 maintain(run_skill) 위주.

출력은 JSON 하나만:
{
  "intent": "say|save_note|run_skill|create_skill|sleep|stop",
  "say": "say일 때만",
  "note": {
    "title": "짧은 제목",
    "body": "2~10문장(필요하면 A:/B: 대화 형식 가능)",
    "tags": ["..."],
    "questions": ["..."]
  },
  "skill_id": "run_skill일 때만",
  "vars": { "k":"v" },
  "requirement": "create_skill일 때만",
  "ms": 1200
}

stop 규칙:
- stop은 사용자가 명시적으로 종료를 요청한 경우에만.
""";
    }

    private static readonly TimeSpan SoloLoopTick = TimeSpan.FromSeconds(10);

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

    private async Task SoloLoopAsync(CancellationToken ct)
    {
        const int MAX_STEPS = 500;
        const int BASE_DELAY_MS = 650;
        const int MAX_ERROR_STREAK = 5;

        int step = 0;
        int errorStreak = 0;

        while (!ct.IsCancellationRequested && step < MAX_STEPS)
        {
            step++;

            try
            {
                await UiAsync(() => MaybeExpireTopic());

                SetSoloStatus("생각 중…");

                var (idle, newUserText, recentContext, topicMode, topicSeed) = await UiAsync(() => SnapshotSoloInputs());

                await TryRunSoloGenerationAsync(async innerCt =>
                {
                    await RunWithTimeoutAsync(async ct2 =>
                    {
                        var track = PickNextTrack(idle);

                        if (idle)
                            await Task.Delay(2000, ct2);

                        var vaultSnippets = Host.VaultIndex.BuildSnippetsBlockForPrompt(6);
                        var hasSnippets = !(vaultSnippets.Contains("없음") || vaultSnippets.Contains("없습니다"));

                        if (idle && !hasSnippets && track == "reflect")
                            track = "maintain";

                        var repeatCandidateBlock = "";

                        var prompt = BuildSoloPrompt(
                            idle, track, newUserText, recentContext,
                            Host.GetSkillSummaries(),
                            vaultSnippets,
                            repeatCandidateBlock,
                            topicMode,
                            topicSeed
                        );

                        try { javis.App.Kernel?.Logger?.Log("solo.llm.request", new { step, idle, track }); } catch { }

                        var raw = await Llm.GenerateAsync(prompt, ct2);
                        var json = ExtractFirstJsonObject(raw);

                        try { javis.App.Kernel?.Logger?.Log("solo.llm.response", new { step, idle, track, json }); } catch { }

                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        var intent = root.TryGetProperty("intent", out var iEl) ? (iEl.GetString() ?? "say") : "say";

                        if (intent == "say")
                        {
                            var text = root.TryGetProperty("say", out var sEl) ? (sEl.GetString() ?? "") : "";

                            if (idle)
                            {
                                await Task.Delay(900, ct2);
                                MarkTrack(track);
                                return;
                            }

                            if (!string.IsNullOrWhiteSpace(text))
                                await UiAsync(() => AppendAssistant($"??? {text}"));
                        }
                        else if (intent == "save_note")
                        {
                            if (!Host.SoloLimiter.CanWriteNow())
                            {
                                await Task.Delay(1200, ct2);
                                MarkTrack(track);
                                return;
                            }

                            var noteEl = root.GetProperty("note");
                            var title = noteEl.TryGetProperty("title", out var tEl) ? (tEl.GetString() ?? "노트") : "노트";
                            var body = noteEl.TryGetProperty("body", out var bEl) ? (bEl.GetString() ?? "") : "";

                            if (IsRepeatNote(title, body))
                            {
                                if (_soloRepeatHit >= 2)
                                {
                                    _soloTopicMode = SoloTopicMode.FreePlay;
                                    _soloTopicSeed = "";
                                    _soloTopicSeedAt = DateTimeOffset.Now;
                                }

                                await Task.Delay(7000, ct2);
                                return;
                            }

                            var tags = ReadStringArray(noteEl, "tags", 8, 40);
                            var qs = ReadStringArray(noteEl, "questions", 4, 180);

                            await Host.SoloNotes.AppendAsync("note", new { title, body, tags, questions = qs }, ct2);
                            Host.SoloLimiter.MarkWrote();

                            var shouldPromote = tags.Any(t => t.Equals("canon", StringComparison.OrdinalIgnoreCase));
                            if (shouldPromote)
                                await Host.Canon.AppendAsync(title, body, tags.ToArray(), kind: "promoted", ct: ct2);

                            var chatText =
                                $"?? [SOLO 노트]\n{title}\n\n{TrimMax(body, 350)}" +
                                (qs.Count > 0 ? "\n\n? 질문\n- " + string.Join("\n- ", qs) : "") +
                                (tags.Count > 0 ? "\n\n??? " + string.Join(", ", tags) : "");

                            await UiAsync(() => AppendAssistant(chatText));
                        }
                        else if (intent == "run_skill")
                        {
                            var skillId = root.GetProperty("skill_id").GetString() ?? "";
                            var vars = ReadVars(root);
                            await UiAsync(() => AppendAssistant($"?? (solo) 스킬 실행: {skillId}"));
                            await Host.RunSkillByIdAsync(skillId, vars, ct2);
                        }
                        else if (intent == "create_skill")
                        {
                            var now = DateTimeOffset.Now;
                            if (_soloCreateCount >= MAX_CREATES_PER_SESSION || (now - _soloLastCreateAt) < CreateCooldown)
                            {
                                await UiAsync(() => AppendAssistant("?? (solo) 기능 생성은 잠깐 쉬고, 아이디어는 노트로 축적할게."));
                                MarkTrack(track);
                                return;
                            }

                            var req = root.GetProperty("requirement").GetString() ?? "";
                            await UiAsync(() => AppendAssistant($"??? (solo) 기능 생성: {req}"));

                            var (skillFile, pluginFile) = await Host.CreateSkillAsync(req);
                            _soloCreateCount++;
                            _soloLastCreateAt = now;

                            await UiAsync(() => AppendAssistant(
                                pluginFile is null
                                    ? $"? 생성 완료: {skillFile}"
                                    : $"? 생성 완료: {skillFile} (+ {pluginFile})"));
                        }
                        else if (intent == "sleep")
                        {
                            var ms = root.TryGetProperty("ms", out var msEl) ? msEl.GetInt32() : 0;
                            ms = Math.Clamp(ms, 3000, 55_000);
                            SetSoloStatus($"대기 {ms}ms…");
                            await Task.Delay(ms, ct2);
                        }
                        else if (intent == "stop")
                        {
                            await UiAsync(() => AppendAssistant("(solo) 자체 종료할게."));
                            await UiAsync(() => SoloToggle.IsChecked = false);
                        }
                        else
                        {
                            await UiAsync(() => AppendAssistant($"(solo) 알 수 없는 intent: {intent}"));
                        }

                        MarkTrack(track);
                    }, TimeSpan.FromSeconds(90), innerCt);
                }, ct);

                errorStreak = 0;
                await Task.Delay(SoloLoopTick, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                errorStreak++;
                await UiAsync(() => AppendAssistant($"(solo 오류) {ex.Message}"));
                SetSoloStatus($"오류 ({errorStreak}/{MAX_ERROR_STREAK})");

                try { javis.App.Kernel?.Logger?.Log("solo.error", new { step, error = ex.Message, stack = ex.ToString() }); } catch { }

                if (errorStreak >= MAX_ERROR_STREAK)
                {
                    await UiAsync(() => AppendAssistant("오류가 반복되어 SOLO를 자동 종료합니다."));
                    await UiAsync(() => SoloToggle.IsChecked = false);
                    break;
                }

                await Task.Delay(1200, ct);
            }
        }

        if (step >= MAX_STEPS)
        {
            await UiAsync(() => AppendAssistant($"SOLO 최대 반복({MAX_STEPS}) 도달로 자동 종료합니다."));
            await UiAsync(() => SoloToggle.IsChecked = false);
        }
    }

    private static Dictionary<string, string> ReadVars(JsonElement root)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("vars", out var varsEl) && varsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in varsEl.EnumerateObject())
                vars[p.Name] = p.Value.ValueKind == JsonValueKind.String ? (p.Value.GetString() ?? "") : p.Value.ToString();
        }

        return vars;
    }

    private static string ExtractFirstJsonObject(string s)
    {
        var start = s.IndexOf('{');
        if (start < 0) throw new Exception("JSON \uC2DC\uC791 '{' \uC5C6\uC74C");

        int depth = 0;
        for (int i = start; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}')
            {
                depth--;
                if (depth == 0) return s.Substring(start, i - start + 1);
            }
        }
        throw new Exception("JSON \uCD94\uCD9C \uC2E4\uD328");
    }

    private static Task UiAsync(Action a)
        => Application.Current.Dispatcher.InvokeAsync(a, DispatcherPriority.Background).Task;

    private static Task<T> UiAsync<T>(Func<T> f)
        => Application.Current.Dispatcher.InvokeAsync(f, DispatcherPriority.Background).Task;

    private async void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        // Shift+Enter => 줄바꿈 허용
        if (Keyboard.Modifiers == ModifierKeys.Shift)
            return;

        // Enter => 전송
        e.Handled = true;

        // Busy거나 빈 입력이면 무시
        if (_vm.IsBusy) return;
        if (string.IsNullOrWhiteSpace(_vm.InputText)) return;

        await _vm.SendAsync();
    }

    private void ScrollToEnd()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (ChatList.Items.Count > 0)
                ChatList.ScrollIntoView(ChatList.Items[^1]);
        });
    }

    private JarvisKernel Kernel => javis.App.Kernel;

    private void OpenPersonaFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = Kernel.Persona.PersonaDir;

        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo
        {
            FileName = dir,
            UseShellExecute = true
        });

        ((ChatViewModel)DataContext).Messages.Add(new javis.Models.ChatMessage("assistant", $"\uD83D\uDCC2 Persona folder opened: {dir}"));
    }

    private void ReloadPersona_Click(object sender, RoutedEventArgs e)
    {
        Kernel.Persona.Reload();

        ((ChatViewModel)DataContext).Messages.Add(new javis.Models.ChatMessage(
            "assistant",
            "\u2705 Persona reloaded (core/chat/solo txt)"
        ));
    }

    private async void ImportFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select files to import into Jarvis",
            Multiselect = true,
            Filter =
                "All files (*.*)|*.*|" +
                "Documents (*.txt;*.md;*.docx;*.pdf)|*.txt;*.md;*.docx;*.pdf|" +
                "Images (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp"
        };

        if (dlg.ShowDialog() != true) return;

        ((ChatViewModel)DataContext).Messages.Add(new javis.Models.ChatMessage("assistant", $"? Importing {dlg.FileNames.Length} files..."));

        try
        {
            var imported = await Kernel.Vault.ImportAsync(dlg.FileNames);

            var indexedCount = await Kernel.VaultIndex.IndexNewAsync(imported, maxFiles: 10);

            ((ChatViewModel)DataContext).Messages.Add(new javis.Models.ChatMessage("assistant", $"?? Indexed: {indexedCount} files"));

            var lines = imported
                .Select(x => $"- {x.fileName} ({x.sizeBytes} bytes, {x.ext})")
                .ToList();

            ((ChatViewModel)DataContext).Messages.Add(new javis.Models.ChatMessage(
                "assistant",
                "\u2705 Import complete\n" + string.Join("\n", lines) +
                $"\n\nSaved to: {Kernel.Vault.InboxDir}"
            ));
        }
        catch (Exception ex)
        {
            ((ChatViewModel)DataContext).Messages.Add(new javis.Models.ChatMessage("assistant", $"? Import failed: {ex.Message}"));
        }
    }

    private async void ChatRoot_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;

        ((ChatViewModel)DataContext).Messages.Add(new javis.Models.ChatMessage("assistant", $"\u23F3 Importing dropped files ({files.Length})..."));

        try
        {
            var imported = await Kernel.Vault.ImportAsync(files);
            var indexedCount = await Kernel.VaultIndex.IndexNewAsync(imported, maxFiles: 10);
            ((ChatViewModel)DataContext).Messages.Add(new javis.Models.ChatMessage("assistant", $"\uD83D\uDD0E Indexed: {indexedCount} files"));
            ((ChatViewModel)DataContext).Messages.Add(new javis.Models.ChatMessage("assistant", $"\u2705 Dropped files saved to: {Kernel.Vault.InboxDir}"));
        }
        catch (Exception ex)
        {
            ((ChatViewModel)DataContext).Messages.Add(new javis.Models.ChatMessage("assistant", $"? Drop import failed: {ex.Message}"));
        }
    }

    private async void ExportSft_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "SFT 데이터셋 저장",
            Filter = "JSONL (*.jsonl)|*.jsonl",
            FileName = $"jarvis-sft-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl"
        };

        if (dlg.ShowDialog() != true) return;

        var vm = (ChatViewModel)DataContext;
        vm.Messages.Add(new javis.Models.ChatMessage("assistant", "? SFT 데이터셋 생성 중…"));

        try
        {
            var n = await Host.Exporter.ExportChatMlJsonlAsync(
                outputPath: dlg.FileName,
                includeCanon: true,
                includeSoloNotes: true,
                maxCanonItems: 800,
                maxNotesItems: 400,
                ct: CancellationToken.None);

            vm.Messages.Add(new javis.Models.ChatMessage("assistant", $"? SFT 생성 완료: {n} samples\n{dlg.FileName}"));
        }
        catch (Exception ex)
        {
            vm.Messages.Add(new javis.Models.ChatMessage("assistant", $"? SFT 생성 실패: {ex.Message}"));
        }
    }

    static string TrimMax(string s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim();
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }

    static List<string> ReadStringArray(JsonElement root, string name, int take, int maxLen)
    {
        var list = new List<string>();
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array) return list;
        foreach (var it in el.EnumerateArray())
        {
            var s = it.ValueKind == JsonValueKind.String ? (it.GetString() ?? "") : it.ToString();
            s = TrimMax(s, maxLen);
            if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
            if (list.Count >= take) break;
        }
        return list;
    }
}
