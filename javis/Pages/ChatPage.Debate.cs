using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using javis.Services;

namespace javis.Pages;

public partial class ChatPage : Page
{
    private readonly SemaphoreSlim _duoGate = new(1, 1);

    private async Task StartDuoRunAsync(string userText)
    {
        await _duoGate.WaitAsync();
        try
        {
            try
            {
                javis.Services.Inbox.DailyInbox.Append(javis.Services.Inbox.InboxKinds.DuoRequest, new
                {
                    evt = "DuoRequestStarted",
                    at = DateTimeOffset.Now,
                    room = "duo"
                });
            }
            catch { }

            // 1) stop previous run
            try { _duoCts?.Cancel(); } catch { }
            if (_duoTask != null)
            {
                try { await _duoTask; } catch { }
            }
            try { _duoCts?.Dispose(); } catch { }

            _duoCts = new CancellationTokenSource();
            var ct = _duoCts.Token;

            // 2) start new run (no Task.Run; GenerateAsync is already async)
            _duoTask = RunDuoAsync(userText, ct);
        }
        finally
        {
            _duoGate.Release();
        }
    }

    private async Task RunDuoAsync(string userText, CancellationToken ct)
    {
        try
        {
            // Snapshot messages on UI thread to avoid cross-thread access
            var recentContext = await UiAsync(() =>
            {
                var vm = (javis.ViewModels.ChatViewModel)DataContext;
                var snap = vm.DuoMessages.ToList();
                return ChatPage.BuildRecentContext(snap);
            });

            var topicMode = "FollowLastTopic";
            var topicSeed = "";

            var vaultSnippets = Host.VaultIndex.BuildSnippetsBlockForPrompt(6);

            var json = await RunDialogueAndGetFinalJsonAsync(
                idle: false,
                track: "debate",
                newUserText: (userText ?? "").Trim(),
                recentContext: recentContext,
                topicMode: topicMode,
                topicSeed: topicSeed,
                vaultSnippets: vaultSnippets,
                ct: ct);

            ct.ThrowIfCancellationRequested();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var intent = root.TryGetProperty("intent", out var iEl) ? (iEl.GetString() ?? "say") : "say";

            if (string.Equals(intent, "say", StringComparison.OrdinalIgnoreCase))
            {
                var say = root.TryGetProperty("say", out var sEl) ? (sEl.GetString() ?? "") : "";
                if (!string.IsNullOrWhiteSpace(say))
                {
                    await UiAsync(() =>
                    {
                        var vm = (javis.ViewModels.ChatViewModel)DataContext;
                        vm.DuoMessages.Add(new javis.Models.ChatMessage("assistant", ChatTextUtil.SanitizeUiText(say)));
                    });

                    try
                    {
                        javis.Services.Inbox.DailyInbox.Append(javis.Services.Inbox.InboxKinds.ChatMessage, new
                        {
                            room = "duo",
                            role = "assistant",
                            text = say,
                            ts = DateTimeOffset.Now
                        });
                    }
                    catch { }
                }
            }
            else
            {
                await UiAsync(() =>
                {
                    var vm = (javis.ViewModels.ChatViewModel)DataContext;
                    vm.DuoMessages.Add(new javis.Models.ChatMessage("assistant", $"(duo) unsupported intent: {intent}"));
                });

                try
                {
                    javis.Services.Inbox.DailyInbox.Append(javis.Services.Inbox.InboxKinds.ChatMessage, new
                    {
                        room = "duo",
                        role = "assistant",
                        text = $"(duo) unsupported intent: {intent}",
                        ts = DateTimeOffset.Now
                    });
                }
                catch { }
            }
        }
        finally
        {
            try
            {
                javis.Services.Inbox.DailyInbox.Append(javis.Services.Inbox.InboxKinds.DuoRequest, new
                {
                    evt = "DuoRequestEnded",
                    at = DateTimeOffset.Now,
                    room = "duo"
                });
            }
            catch { }
        }
    }

    private async Task<string> RunDialogueAndGetFinalJsonAsync(
        bool idle,
        string track,
        string newUserText,
        string recentContext,
        string topicMode,
        string topicSeed,
        string vaultSnippets,
        CancellationToken ct)
    {
        var focus = !string.IsNullOrWhiteSpace(newUserText) ? newUserText : topicSeed;
        if (string.IsNullOrWhiteSpace(focus)) focus = recentContext;

        var aPrompt = $"""
{Host.Persona.CoreText}

너는 Agent A(제안자)야.
목표: 아래 주제에 대해 "실행 가능한 제안"을 3개 제시하고, 각각 근거와 예상 리스크를 짧게 덧붙여.
불필요한 메타 문선 금지. JSON 금지. 한국어로.

[주제]
{focus}

[최근 대화]
{recentContext}

[자료 스니펫]
{vaultSnippets}
""";

        var a = await LlmA.GenerateAsync(aPrompt, ct);

        var bPrompt = $"""
{Host.Persona.CoreText}

너는 Agent B(비판자)야.
목표: Agent A의 제안을 비판하고(반례/리스크/누락), 더 나은 대안 또는 수정안을 2~3개 제시해.
불필요한 메타 문선 금지. JSON 금지. 한국어로.

[주제]
{focus}

[Agent A 제안]
{a}
""";
        var b = await LlmB.GenerateAsync(bPrompt, ct);

        if (_debateShow)
        {
            await UiAsync(() =>
            {
                var vm = (javis.ViewModels.ChatViewModel)DataContext;
                vm.DuoMessages.Add(new javis.Models.ChatMessage("assistant_a", javis.Services.ChatTextUtil.SanitizeUiText(a ?? "")));
                vm.DuoMessages.Add(new javis.Models.ChatMessage("assistant_b", javis.Services.ChatTextUtil.SanitizeUiText(b ?? "")));
            });
        }

        var modPrompt = $$"""
{Host.Persona.CoreText}

{Host.Persona.SoloOverlayText}

너는 Moderator야. 아래를 종합해서 "최종 결론"을 만들어.
출력은 반드시 JSON 하나만, 아래 스키마를 따를 것.

중요:
- 사용자의 새 입력에 "메모/노트/정리/기억/저장/note/memo/remember" 같은 요청이 없으면 intent는 say 또는 sleep으로.
- save_note는 정말 필요할 때만.

idle={idle}
track={track}
topic_mode={topicMode}

[새 사용자 입력]
{newUserText}

[최근 대화]
{recentContext}

[TOPIC SEED]
{topicSeed}

[Agent A]
{a}

[Agent B]
{b}

JSON 스키마(그대로 따를 것):
- intent: say|save_note|run_skill|create_skill|sleep|stop
- say: say일 때만
- note: { title, body, tags[], questions[] } (save_note일 때만)
- skill_id: run_skill일 때만
- vars: object
- requirement: create_skill일 때만
- ms: sleep일 때만
""";

        var raw = await LlmA.GenerateAsync(modPrompt, ct);
        return javis.Services.JsonUtil.ExtractFirstJsonObject(raw);
    }
}
