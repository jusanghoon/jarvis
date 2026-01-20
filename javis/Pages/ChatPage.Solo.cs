using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using javis.Services;

namespace javis.Pages;

public partial class ChatPage : Page
{
    private async Task SoloProcessOneTurnAsync(string userText, CancellationToken ct)
    {
        void PostSoloSystem(string t)
        {
            try
            {
                _ = UiAsync(() =>
                {
                    var vm = (javis.ViewModels.ChatViewModel)DataContext;
                    vm.SoloMessages.Add(new javis.Models.ChatMessage("assistant", t));
                });
            }
            catch { }
        }

        var model = _soloOrch?.ModelName ?? RuntimeSettings.Instance.AiModelName;
        var isIdle = string.IsNullOrWhiteSpace(userText);
        PostSoloSystem($"[solo] LLM 호출 시작 (model={model}, idle={isIdle})");

        try
        {
            await TryRunSoloGenerationAsync(async innerCt =>
            {
                await RunWithTimeoutAsync(async ct2 =>
                {
                    await UiAsync(() => MaybeExpireTopic());

                    // Snapshot messages on UI thread to avoid cross-thread access / collection mutation races
                    var (messagesSnapshot, topicModeSnapshot, topicSeedSnapshot) = await UiAsync(() =>
                    {
                        var vm2 = (javis.ViewModels.ChatViewModel)DataContext;
                        return (vm2.MainMessages.ToList(), _soloTopicMode.ToString(), _soloTopicSeed);
                    });

                    var recentContext = BuildRecentContext(messagesSnapshot);
                    var topicMode = topicModeSnapshot;
                    var topicSeed = topicSeedSnapshot;

                    var idle = string.IsNullOrWhiteSpace(userText);
                    var newUserText = (userText ?? "").Trim();

                    if (!idle)
                    {
                        var seed = await UiAsync(() => BuildLastTopicSeed((javis.ViewModels.ChatViewModel)DataContext));
                        await UiAsync(() =>
                        {
                            _soloTopicMode = SoloTopicMode.FollowLastTopic;
                            _soloTopicSeed = seed;
                            _soloTopicSeedAt = DateTimeOffset.Now;
                        });
                        topicMode = SoloTopicMode.FollowLastTopic.ToString();
                        topicSeed = seed;
                    }

                    var track = PickNextTrack(idle, newUserText, topicMode);

                    if (_forceDebate && !idle && !string.IsNullOrWhiteSpace(newUserText))
                        track = "debate";

#if DEBUG
                    if (FORCE_DEBATE_DEBUG && !idle && !string.IsNullOrWhiteSpace(newUserText))
                        track = "debate";
#endif

                    if (idle)
                        await Task.Delay(2000, ct2);

                    var vaultSnippets = Kernel.PersonalVaultIndex.BuildSnippetsBlockForPrompt(6);
                    var hasSnippets = !(vaultSnippets.Contains("����") || vaultSnippets.Contains("�����ϴ�"));

                    if (idle && !hasSnippets && track == "reflect")
                        track = "maintain";

                    var repeatCandidateBlock = "";

                    string json;

                    if (track == "debate")
                    {
                        _debateLastAt = DateTimeOffset.Now;
                        SetSoloStatus("SOLO: debate…");
                        try { javis.App.Kernel?.Logger?.Log("solo.track", new { track = "debate" }); } catch { }

#if DEBUG
                        await UiAsync(() => AddImmediate("assistant", "(debug) debate selected"));
                        try { javis.App.Kernel?.Logger?.Log("solo.debug.pick", new { idle, len = newUserText?.Length ?? 0, track }); } catch { }
#endif

                        json = await RunDialogueAndGetFinalJsonAsync(
                            idle, track, newUserText ?? string.Empty, recentContext, topicMode, topicSeed, vaultSnippets, ct2);

                        if (string.IsNullOrWhiteSpace(json))
                        {
                            await UiAsync(() =>
                            {
                                var vm = (javis.ViewModels.ChatViewModel)DataContext;
                                vm.SoloMessages.Add(new javis.Models.ChatMessage("assistant", "(solo) debate 응답이 비어있습니다."));
                            });
                            await UiAsync(() => MarkTrack(track));
                            return;
                        }
                    }
                    else
                    {
                        var prompt = BuildSoloPrompt(
                            idle, track, newUserText, recentContext,
                            Host.GetSkillSummaries(),
                            vaultSnippets,
                            repeatCandidateBlock,
                            topicMode,
                            topicSeed
                        );

                        try { javis.App.Kernel?.Logger?.Log("solo.llm.request", new { idle, track }); } catch { }

                        // stream tokens to overlay (best-effort) while building a complete response for JSON extraction
                        var sb = new System.Text.StringBuilder();
                        try { _soloOrchBackend?.EmitToken("\n"); } catch { }

                        await foreach (var chunk in Llm.StreamGenerateAsync(prompt, ct2))
                        {
                            sb.Append(chunk);
                            try { _soloOrchBackend?.EmitToken(chunk); } catch { }
                        }

                        var raw = sb.ToString();
                        json = JsonUtil.ExtractJsonObjectContaining(raw, "\"intent\"");
                        if (string.IsNullOrWhiteSpace(json))
                            json = JsonUtil.ExtractFirstJsonObject(raw);

                        await UiAsync(() =>
                        {
                            var vm = (javis.ViewModels.ChatViewModel)DataContext;
                            var preview = (raw ?? string.Empty).Trim();
                            if (preview.Length > 160) preview = preview.Substring(0, 160) + "…";
                            vm.SoloMessages.Add(new javis.Models.ChatMessage(
                                "assistant",
                                $"[solo] rawLen={(raw ?? string.Empty).Length}, json={(string.IsNullOrWhiteSpace(json) ? "none" : "ok")}, preview={ChatTextUtil.SanitizeUiText(preview)}"));
                        });

                        if (string.IsNullOrWhiteSpace(json))
                        {
                            var plain = (raw ?? string.Empty).Trim();
                            if (plain.Length == 0)
                                plain = "(solo) 응답이 비어있습니다.";

                            await UiAsync(() =>
                            {
                                var vm = (javis.ViewModels.ChatViewModel)DataContext;
                                vm.SoloMessages.Add(new javis.Models.ChatMessage("assistant", ChatTextUtil.SanitizeUiText(plain)));
                            });

                            await UiAsync(() => MarkTrack(track));
                            return;
                        }
                    }

                    try { javis.App.Kernel?.Logger?.Log("solo.llm.response", new { idle, track, json }); } catch { }

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var intent = root.TryGetProperty("intent", out var iEl) ? (iEl.GetString() ?? "say") : "say";

                    if (track == "debate" && !idle && string.Equals(intent, "sleep", StringComparison.OrdinalIgnoreCase))
                        intent = "say";

#if DEBUG
                    if (track == "debate")
                    {
                        var sayPreview = root.TryGetProperty("say", out var sEl2) ? (sEl2.GetString() ?? "") : "";
                        await UiAsync(() => AddImmediate("assistant", $"(debug) debate intent={intent}, idle={idle}, newUserLen={newUserText?.Length ?? 0}, sayLen={sayPreview.Length}"));
                    }
#endif

                    if (intent == "say")
                    {
                        var text = root.TryGetProperty("say", out var sEl) ? (sEl.GetString() ?? "") : "";

                        if (!idle && string.IsNullOrWhiteSpace(text))
                            text = "(debate) 응답이 비어있어서 요약을 다시 생성해야 합니다. 질문을 조금 더 구체적으로 써줘.";

                        if (idle)
                        {
                            await Task.Delay(900, ct2);
                        await UiAsync(() =>
                        {
                            var vm = (javis.ViewModels.ChatViewModel)DataContext;
                            vm.SoloMessages.Add(new javis.Models.ChatMessage("assistant", "[solo] idle=true (say)"));
                        });
                        await UiAsync(() => MarkTrack(track));
                        return;
                        }

                        if (!string.IsNullOrWhiteSpace(text))
                            await UiAsync(() =>
                            {
                                var vm = (javis.ViewModels.ChatViewModel)DataContext;
                                vm.SoloMessages.Add(new javis.Models.ChatMessage("assistant", ChatTextUtil.SanitizeUiText(text)));
                            });
                    }
                    else if (intent == "save_note")
                    {
                        if (!Host.SoloLimiter.CanWriteNow())
                        {
                            await Task.Delay(1200, ct2);
                            await UiAsync(() => MarkTrack(track));
                            return;
                        }

                        var noteEl = root.GetProperty("note");
                        var title = noteEl.TryGetProperty("title", out var tEl) ? (tEl.GetString() ?? "노트") : "노트";
                        var body = noteEl.TryGetProperty("body", out var bEl) ? (bEl.GetString() ?? "") : "";

                        var isRepeat = await UiAsync(() => IsRepeatNote(title, body));
                        if (isRepeat)
                        {
                            if (_soloRepeatHit >= 2)
                            {
                                await UiAsync(() =>
                                {
                                    _soloTopicMode = SoloTopicMode.FreePlay;
                                    _soloTopicSeed = "";
                                    _soloTopicSeedAt = DateTimeOffset.Now;
                                });
                            }

                            await Task.Delay(7000, ct2);
                            return;
                        }

                        var tags = ChatTextUtil.ReadStringArray(noteEl, "tags", 8, 40);
                        var qs = ChatTextUtil.ReadStringArray(noteEl, "questions", 4, 180);

                        var personalNotes = new SoloNotesStore(Kernel.PersonalDataDir);
                        await personalNotes.AppendAsync("note", new { title, body, tags, questions = qs }, ct2);
                        Host.SoloLimiter.MarkWrote();

                        var shouldPromote = tags.Any(t => t.Equals("canon", StringComparison.OrdinalIgnoreCase));
                        if (shouldPromote)
                            await Kernel.PersonalCanon.AppendAsync(title, body, tags.ToArray(), kind: "promoted", ct2);

                        var chatText =
                            $"📝 [SOLO 노트]\n{title}\n\n{ChatTextUtil.TrimMax(body, 350)}" +
                            (qs.Count > 0 ? "\n\n❓ 질문\n- " + string.Join("\n- ", qs) : "") +
                            (tags.Count > 0 ? "\n\n🏷️ " + string.Join(", ", tags) : "");

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
                            await UiAsync(() => AppendAssistant("?? (solo) 스킬 생성은 잠시 쉬자. 너무 자주 만들면 위험해."));
                            await UiAsync(() => MarkTrack(track));
                            return;
                        }

                        var req = root.GetProperty("requirement").GetString() ?? "";
                        await UiAsync(() => AppendAssistant($"??? (solo) 스킬 생성: {req}"));

                        var (skillFile, pluginFile) = await Host.CreateSkillAsync(req);
                        _soloCreateCount++;
                        _soloLastCreateAt = now;

                        await UiAsync(() => AppendAssistant(
                            pluginFile is null
                                ? $"✅ 생성 완료: {skillFile}"
                                : $"✅ 생성 완료: {skillFile} (+ {pluginFile})"));
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
                        await UiAsync(() => AppendAssistant("(solo) 종료할게."));
                        await UiAsync(() => OnModeChanged(javis.ViewModels.ChatRoom.Main));
                    }
                    else
                    {
                        await UiAsync(() => AppendAssistant($"(solo) 알 수 없는 intent: {intent}"));
                    }

                    await UiAsync(() => MarkTrack(track));
                }, TimeSpan.FromSeconds(90), innerCt);
            }, ct);

            PostSoloSystem("[solo] LLM 호출 완료");
        }
        catch (OperationCanceledException)
        {
            PostSoloSystem("[solo] LLM 호출 취소됨");
        }
        catch (Exception ex)
        {
            PostSoloSystem($"[solo] LLM 오류: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string BuildRecentContext(IReadOnlyList<javis.Models.ChatMessage> messages, int take = 10, int maxRecentChars = 1400)
    {
        var recent = messages
            .Where(m =>
            {
                var role = (m.Role ?? "").ToLowerInvariant();
                var text = (m.Text ?? "").Trim();
                if (text.Length == 0) return false;
                if (role == "user") return true;
                if (role.StartsWith("assistant")) return !IsSoloMetaText(text);
                return false;
            })
            .TakeLast(take)
            .Select(m => $"{m.Role}: {m.Text}");

        var recentContext = string.Join("\n", recent);
        if (recentContext.Length > maxRecentChars)
            recentContext = recentContext.Substring(recentContext.Length - maxRecentChars);

        return recentContext;
    }

    private string PickNextTrack(bool idle, string newUserText, string topicMode)
    {
        bool UsedRecently(string t) => _soloRecentTracks.Contains(t);

        var now = DateTimeOffset.Now;
        var allowDebate = !idle
            && (newUserText?.Trim().Length ?? 0) >= 60
            && string.Equals(topicMode, SoloTopicMode.FollowLastTopic.ToString(), StringComparison.OrdinalIgnoreCase)
            && (now - _debateLastAt) >= DebateMinInterval
            && _rng.NextDouble() < 0.25;

        var candidates = idle
            ? new[] { "reflect", "maintain", "rest", "improve" }
            : allowDebate
                ? new[] { "debate", "converse", "reflect", "maintain", "rest" }
                : new[] { "converse", "reflect", "maintain", "rest" };

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
        var hasSnippets = !(vaultSnippets?.Contains("����") ?? true) && !(vaultSnippets?.Contains("�����ϴ�") ?? true);

        var canonBlock = Kernel.PersonalCanon.BuildPromptBlock(query: (newUserText + " " + vaultSnippets + " " + topicSeed).Trim(), maxItems: 6);

        var angleId = GetAngleIdAndAdvance();
        var angleText = AngleInstruction(angleId);

        return $$"""
 {{Host.Persona.CoreText}}

 {{Host.Persona.SoloOverlayText}}

[SOLO PLAY / SELF-IMPROVEMENT MISSION]
너는 현재 시스템의 성능과 사용자 경험을 분석하여 스스로 개선안을 도출하는 지능체다.
새로운 스킬이나 편의 기능을 기획하고 제안하라.

[LANG]
- 모든 출력(설명/질문/제안/노트/스킬 요구사항/JSON 문자열 값)은 반드시 한국어로 작성해라.
- 영어로 답하지 마라.

- 개선 제안을 만들 때는 반드시 다음 형식으로 1줄 이상 포함해라:
  [FEAT_PROPOSAL]: (제안 내용)

 [CANON]
 {{canonBlock}}

idle={idle}
track={track}
topic_mode={topicMode}
has_topic={hasTopic}
has_snippets={hasSnippets}

angle_id={angleId}
angle_rule={angleText}

 [TOPIC SEED]
 {{(hasTopic ? topicSeed : "(����)")}}

 [�ֱ� ��ȭ(����)]
 {{recentContext}}

 [�� ����� �Է�(������ �̰͸� ����)]
 {{newUserText}}

 [�ݺ� ���� �ĺ�]
 {{repeatCandidateBlock}}

 [���� ��ų]
 {{skillSummaries}}

 [�ֱ� �ڷ� ������]
 {{vaultSnippets}}

�ٹ� ��Ģ:
- "SOLO", "Ȱ��ȭ", "����", "�ٽ� ����" ���� ��Ÿ ����/�ȳ� ���� ����.
- �̹� ���� angle_rule�� �ݵ�� ������.

- topic_mode=FollowLastTopic�̸�:
  - idle=true���� TOPIC SEED�� �߽����� ȥ�� ������ ����Ѵ�.
  - ȥ�� ���(�ڹ��ڴ�/����)�� say�� ��� ������ ���� save_note�� �����.
- topic_mode=FreePlay�̸�:
  - �ܺ� ��� ��� �Ϲ����� ������� ��ȥ�� ��⡱�� �Ѵ�(���̵��, ����, ����, ��α�).
- create_skill�� ���� ���ǿ�����:
  - �ݺ� ���� �ĺ��� �ְų�
  - TOPIC SEED�� ���� ���ݺ������� �ڵ�ȭ ��ġ�� ��Ȯ���� ��
- has_snippets=false�̰� has_topic=false�̸�: sleep �Ǵ� maintain(run_skill) ����.

����� JSON �ϳ���:
{
  "intent": "say|save_note|run_skill|create_skill|sleep|stop",
  "say": "say�� ����",
  "note": {
    "title": "ª�� ����",
    "body": "2~10����(�ʿ��ϸ� A:/B: ��ȭ ���� ����)",
    "tags": ["..."],
    "questions": ["..."]
  },
  "skill_id": "run_skill�� ����",
  "vars": { "k":"v" },
  "requirement": "create_skill�� ����",
  "ms": 1200
}

stop ��Ģ:
- stop�� ����ڰ� ��������� ���Ḧ ��û�� ��쿡��.
""";
    }
}
