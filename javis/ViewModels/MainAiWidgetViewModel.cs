using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jarvis.Core.System;
using javis.Models;
using javis.Services;
using javis.Services.SystemActions;
using javis.Services.MainAi;
using javis.Services.Solo;

namespace javis.ViewModels;

public partial class MainAiWidgetViewModel : ObservableObject, ISoloUiSink
{
    public event Action<string>? RequestNavigate;
    public event Action<string, string?>? RequestAction;

    private readonly MainAiHelpResponder _help;
    private readonly string _codeIndex;
    private readonly string _solutionRoot;
    private readonly MainAiFileRelevanceStore _relevance;

    private SoloOrchestrator? _soloOrch;
    private SoloBackendAdapter? _soloOrchBackend;
    private long _soloMsgId;

    private int _autoStartIssued;

    private CancellationTokenSource? _askCts;

    private readonly IntentResolver _intentResolver = new();

    private string? _pendingFollowUpIntent;

    private string _lastToolFeedback = "";

    // 자동 피드백(b): 같은 태그로 연속 질문하면 직전 선택을 실패로 간주
    private string[] _lastTags = Array.Empty<string>();
    private string[] _lastPickedFiles = Array.Empty<string>();
    private DateTimeOffset _lastAnswerAt;
    private bool _lastRewarded;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    [ObservableProperty] private string _promptText = "";
    [ObservableProperty] private string _answerText = "";
    [ObservableProperty] private bool _isBusy;

    // Manager System overlay / streaming feedback
    [ObservableProperty] private string _thinkingStage = "";

    // explicit system-thinking start command (used by overlay button)
    [ObservableProperty] private bool _isSoloThinkingStarting;

    public bool CanStartSoloThinking => !IsSoloThinkingStarting;

    [RelayCommand(CanExecute = nameof(CanStartSoloThinking))]
    private async Task StartSoloThinkingAsync()
    {
        if (!CanStartSoloThinking) return;

        if (_soloOrch is { IsRunning: true })
            return;

        ThinkingStage = "관리자 권한으로 시스템 사유를 시작합니다...";

        IsSoloThinkingStarting = true;
        try { StartSoloThinkingCommand.NotifyCanExecuteChanged(); } catch { }

        try
        {
            EnsureSoloOrchestrator();
            if (_soloOrch == null) return;

            Debug.WriteLine("[MainAiWidget] StartSoloThinkingCommand invoked");
            await _soloOrch.StartAsync();
            var nextId = Interlocked.Increment(ref _soloMsgId);
            _soloOrch.OnUserMessage(nextId, string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainAiWidget] StartSoloThinkingCommand error: {ex.Message}");
        }
        finally
        {
            IsSoloThinkingStarting = false;
            try { StartSoloThinkingCommand.NotifyCanExecuteChanged(); } catch { }
        }
    }


    private void EnsureSoloOrchestrator()
    {
        if (_soloOrch != null) return;

        var kernel = javis.App.Kernel;
        if (kernel == null)
        {
            Debug.WriteLine("[MainAiWidget] EnsureSoloOrchestrator: Kernel is null");
            return;
        }

        async Task RunOneTurnAsync(string userText, CancellationToken ct)
        {
            try
            {
                // MainAiWidget 로직에서는 과거 UI 페이지에 있던 방대한 1-turn 처리 로직을 직접 복제하지 않고,
                // 시스템 사유 전용 프롬프트를 둔 간단한 1-turn 호출로 동작시킨다.
                // 필요 시 이후 단계에서 해당 로직을 Manager System 서비스로 승격해 재사용한다.

                var model = _soloOrch?.ModelName ?? RuntimeSettings.Instance.AiModelName;
                var prompt = (userText ?? string.Empty).Trim();
                if (prompt.Length == 0)
                    prompt = "(idle)";

                OnProgress($"SOLO 호출 중… (model={model})");

                // 토큰 스트림 리셋 신호
                _soloOrchBackend?.EmitToken("\n");

                var baseUrl = "http://localhost:11434";
                var llm = new javis.Services.OllamaClient(baseUrl, model);

                var sb = new System.Text.StringBuilder();
                await foreach (var tok in llm.StreamGenerateAsync(prompt, ct))
                {
                    _soloOrchBackend?.EmitToken(tok);
                    sb.Append(tok);
                }

                var reply = sb.ToString();
                if (!string.IsNullOrWhiteSpace(reply))
                    PostAssistant(reply.Trim());
            }
            catch (OperationCanceledException)
            {
                PostSystem("SOLO 취소됨");
            }
            catch (Exception ex)
            {
                PostSystem($"SOLO 오류: {ex.Message}");
            }
            finally
            {
                OnComplete(null);
            }
        }

        _soloOrchBackend = new SoloBackendAdapter(RunOneTurnAsync);
        _soloOrch = new SoloOrchestrator(this, _soloOrchBackend)
        {
            ModelName = RuntimeSettings.Instance.AiModelName,
            AutoContinue = true
        };

        _soloOrch.OnTokenReceived += token =>
        {
            try
            {
                if (string.IsNullOrEmpty(token)) return;
                if (token == "\n")
                    ThinkingStage = "";
                else
                    OnToken(token);
            }
            catch { }
        };

        // Auto-start system thinking as soon as the manager widget is created.
        // Guard: issue once per VM instance, and avoid reentry if already running/starting.
        if (Interlocked.Exchange(ref _autoStartIssued, 1) == 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(150);
                    if (IsSoloThinkingStarting) return;
                    if (_soloOrch is { IsRunning: true }) return;
                    StartSoloThinkingCommand.Execute(null);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MainAiWidget] Auto-start SOLO failed: {ex.Message}");
                }
            });
        }
    }

    public void OnToken(string token)
    {
        try
        {
            if (token is null) return;
            ThinkingStage = (ThinkingStage ?? "") + token;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainAiWidget] OnToken error: {ex.Message}");
        }
    }

    public void OnStateChanged(string state)
        => Debug.WriteLine($"[MainAiWidget] SOLO state: {state}");

    public void OnProgress(string text)
    {
        Debug.WriteLine($"[MainAiWidget] SOLO progress: {text}");
    }

    public void OnComplete(string? finalText = null)
    {
        Debug.WriteLine("[MainAiWidget] SOLO complete");
        try
        {
            if (!string.IsNullOrWhiteSpace(finalText))
                ThinkingStage = finalText;
        }
        catch { }
    }

    public void PostSystem(string text)
    {
        Debug.WriteLine($"[MainAiWidget] SOLO system: {text}");
        try
        {
            var t = ChatTextUtil.SanitizeUiText(text);
            Messages.Add(new ChatMessage("system", t));
            ThinkingStage = t;
        }
        catch { }
    }

    public void PostAssistant(string text)
    {
        try
        {
            var t = ChatTextUtil.SanitizeUiText(text);
            Messages.Add(new ChatMessage("assistant", t));
            AnswerText = t;
        }
        catch { }
    }

    public void PostDebug(string text)
        => Debug.WriteLine($"[MainAiWidget] SOLO debug: {text}");

    private sealed class JaeminIntent
    {
        public string? Intent { get; set; }
        public string? Target { get; set; }
        public string? Name { get; set; }
        public string? Date { get; set; }
        public string? Text { get; set; }

        public string? Path { get; set; }
        public string? Hint { get; set; }

        public string[]? Related { get; set; }
    }

    private sealed class FilePickResult
    {
        public string[]? Pick { get; set; }
    }

    private static bool TagsSimilar(string[] a, string[] b)
    {
        if (a.Length == 0 || b.Length == 0) return false;
        var inter = a.Intersect(b, StringComparer.OrdinalIgnoreCase).Count();
        return inter > 0;
    }

    private static bool IsSafeCodePath(string root, string rel)
    {
        try
        {
            rel = (rel ?? "").Replace('\\', '/').Trim();
            if (rel.Length == 0) return false;

            // block obvious traversal
            if (rel.Contains("..", StringComparison.Ordinal)) return false;

            var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, rel));
            root = System.IO.Path.GetFullPath(root);

            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;

            var ext = System.IO.Path.GetExtension(full);
            return ext is ".cs" or ".xaml" or ".csproj" or ".json" or ".md";
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveCodePath(string root, string? path, string? hint)
    {
        // 1) explicit path
        var p = (path ?? "").Trim();
        if (p.Length > 0 && IsSafeCodePath(root, p))
            return p.Replace('\\', '/');
        
        // 2) try to map hint -> a known file name
        var h = (hint ?? "").Trim();
        if (h.Length == 0) return null;

        // If user mentions "viewmodel" try to locate related view model code.
        var file = h;
        if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
            !file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            file += ".cs";

        // Search common locations (bounded)
        try
        {
            var dirs = new[] { "javis/ViewModels", "javis/Pages", "javis/Services", "javis/Models", "Jarvis.Core", "Jarvis.Abstractions", "Jarvis.Modules.Vault" };
            foreach (var d in dirs)
            {
                var candidate = System.IO.Path.Combine(root, d, file);
                if (System.IO.File.Exists(candidate))
                    return (d + "/" + file).Replace('\\', '/');
            }

            // fallback: contains match (limited)
            var all = System.IO.Directory.EnumerateFiles(root, "*.*", System.IO.SearchOption.AllDirectories)
                .Where(f => !f.Contains(System.IO.Path.DirectorySeparatorChar + "bin" + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.Contains(System.IO.Path.DirectorySeparatorChar + "obj" + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                .Take(8000);

            var match = all.FirstOrDefault(f => System.IO.Path.GetFileName(f).Equals(file, StringComparison.OrdinalIgnoreCase) ||
                                               System.IO.Path.GetFileNameWithoutExtension(f).Equals(h, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return System.IO.Path.GetRelativePath(root, match).Replace('\\', '/');
        }
        catch { }

        return null;
    }

    private static string ReadCodeSnippet(string root, string relPath, int maxChars = 18_000)
    {
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relPath));
        var text = System.IO.File.ReadAllText(full);
        text = text.Replace("\r\n", "\n");
        if (text.Length <= maxChars) return text;
        return text.Substring(0, maxChars) + "\n\n…(truncated)";
    }

    public MainAiWidgetViewModel()
    {
        _help = new MainAiHelpResponder(baseUrl: "http://localhost:11434", model: "qwen3:4b");

        var root = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            var parent = System.IO.Directory.GetParent(root);
            if (parent is null) break;
            root = parent.FullName;
            if (System.IO.Directory.Exists(System.IO.Path.Combine(root, "javis"))) break;
        }

        _solutionRoot = root;
        _relevance = new MainAiFileRelevanceStore(root);
        _codeIndex = MainAiCodeIndex.BuildIndexText(root, maxFiles: 140);

        Messages.Add(new ChatMessage("assistant", "재민 온라인. 무엇을 도와줄까?"));
        AnswerText = Messages.LastOrDefault(m => string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))?.Text ?? "";

        MainAiDocBus.Suggestion += p =>
        {
            try
            {
                var t = (p.Text ?? "").Trim();
                if (t.Length == 0) return;

                var msg = new ChatMessage("assistant", t);
                Messages.Add(msg);
                AnswerText = msg.Text;
            }
            catch { }
        };
    }

    [RelayCommand]
    private async Task AskAsync()
    {
        try { _askCts?.Cancel(); } catch { }
        try { _askCts?.Dispose(); } catch { }

        var q = (PromptText ?? "").Trim();
        if (q.Length == 0)
        {
            _askCts = null;
            return;
        }

        // 자동 피드백(b) 보강 (관리자 피드백):
        // - 직전 응답 이후 일정 시간(3분) 동안 같은 태그 재질문이 없으면 성공으로 약하게 보상
        // - 같은 태그로 다시 질문하면 즉시 실패 패널티
        string[] nowTags = Array.Empty<string>();
        try
        {
            nowTags = MainAiCodeIndex.InferTagsFromQuestionPublic(q).ToArray();

            if (!_lastRewarded && _lastTags.Length > 0 && _lastPickedFiles.Length > 0)
            {
                var elapsed = DateTimeOffset.UtcNow - _lastAnswerAt;
                if (elapsed >= TimeSpan.FromMinutes(3) && !TagsSimilar(_lastTags, nowTags))
                {
                    _relevance.RecordFeedback(_lastTags, _lastPickedFiles, helpful: true);
                    _lastRewarded = true;
                }
            }

            if (TagsSimilar(_lastTags, nowTags) && _lastPickedFiles.Length > 0)
            {
                _relevance.RecordFeedback(_lastTags, _lastPickedFiles, helpful: false);
                _lastRewarded = true; // 같은 태그 재질문이면 보상은 더 이상 없음
            }
        }
        catch { }

        PromptText = "";

        IsBusy = true;

        var userMsg = new ChatMessage("user", q);
        Messages.Add(userMsg);

        var pending = new ChatMessage("assistant", "요청 처리 중…");
        Messages.Add(pending);
        AnswerText = pending.Text;

        _askCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        try
        {
            // If the previous turn requested missing fields, try to learn from the user's answer first.
            if (!string.IsNullOrWhiteSpace(_pendingFollowUpIntent) &&
                _intentResolver.TryLearnFromUserText(q, out var updates) &&
                updates.Count > 0)
            {
                try
                {
                    var svc = UserProfileService.Instance;
                    var p = svc.TryGetActiveProfile();
                    if (p != null)
                    {
                        var merged = new System.Collections.Generic.Dictionary<string, string>(p.Fields, StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in updates) merged[kv.Key] = kv.Value;
                        svc.SaveProfile(p with { Fields = merged });
                    }
                }
                catch { }

                // Re-run the original intent using the same user message now that profile is updated.
                try
                {
                    var kernel = javis.App.Kernel;
                    if (kernel != null)
                    {
                        var r2 = await _intentResolver.TryResolveAsync(_pendingFollowUpIntent, UserProfileService.Instance, kernel.SystemActions, _askCts.Token);
                        if (r2.Handled)
                        {
                            if (r2.Execution is ExecutionResult ex2)
                                _lastToolFeedback = BuildToolFeedback(ex2);

                            try
                            {
                                var p = UserProfileService.Instance.TryGetActiveProfile();
                                var deviceId = p?.LastDeviceDiagnostics?.Fingerprint?.DeviceId ?? "unknown";
                                var userId = p?.Id ?? UserProfileService.Instance.ActiveUserId;
                                kernel.Logger.Log(r2.AuditKind ?? "system.intent", new { userId, deviceId, msg = _pendingFollowUpIntent, audit = r2.AuditPayload, success = r2.Execution?.Success, error = r2.Execution?.Error });
                            }
                            catch { }

                            _pendingFollowUpIntent = r2.NeedsUserInput ? _pendingFollowUpIntent : null;
                            pending.Text = string.IsNullOrWhiteSpace(r2.ReplyText) ? "(응답이 비어있음)" : r2.ReplyText;
                            AnswerText = pending.Text;
                            return;
                        }
                    }
                }
                catch { }
            }

            var qWithFeedback = string.IsNullOrWhiteSpace(_lastToolFeedback)
                ? q
                : $"{q}\n\n[시스템 실행 결과]\n{_lastToolFeedback}";

            var raw = await _help.AnswerAsync(qWithFeedback, _codeIndex, _askCts.Token);
            Debug.WriteLine($"[MainAI][RawResponse]\n{raw}");
            var json = javis.Services.JsonUtil.ExtractFirstJsonObject(raw);

            JaeminIntent? env = null;
            try
            {
                env = JsonSerializer.Deserialize<JaeminIntent>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                env = null;
            }

            var intent = (env?.Intent ?? "say").Trim().ToLowerInvariant();
            var text = (env?.Text ?? "").Trim();

            // Profile-based system intent resolution (e.g., "프로젝트 열어줘")
            try
            {
                var kernel = javis.App.Kernel;
                if (kernel != null)
                {
                    var sysRes = await _intentResolver.TryResolveAsync(q, UserProfileService.Instance, kernel.SystemActions, _askCts.Token);
                    if (sysRes.Handled)
                    {
                        if (sysRes.Execution is ExecutionResult ex)
                            _lastToolFeedback = BuildToolFeedback(ex);

                        try
                        {
                            var p = UserProfileService.Instance.TryGetActiveProfile();
                            var deviceId = p?.LastDeviceDiagnostics?.Fingerprint?.DeviceId ?? "unknown";
                            var userId = p?.Id ?? UserProfileService.Instance.ActiveUserId;
                            kernel.Logger.Log(sysRes.AuditKind ?? "system.intent", new { userId, deviceId, msg = q, audit = sysRes.AuditPayload, success = sysRes.Execution?.Success, error = sysRes.Execution?.Error });
                        }
                        catch { }

                        if (sysRes.NeedsUserInput)
                            _pendingFollowUpIntent = q;
                        else
                            _pendingFollowUpIntent = null;

                        pending.Text = string.IsNullOrWhiteSpace(sysRes.ReplyText) ? "(응답이 비어있음)" : sysRes.ReplyText;
                        AnswerText = pending.Text;
                        return;
                    }

                    kernel.Logger.Log("intent.resolve.failed", new { msg = $"해석 실패: {q}", rawResponse = raw });
                }
            }
            catch { }

            if (intent == "read_code")
            {
                // B 전략: 후보 8개 -> 모델이 핵심 파일을 고르게 -> 선택된 파일로 분석
                var tags = MainAiCodeIndex.InferTagsFromQuestionPublic(q).ToArray();

                // 후보 생성: 태그 기반 + 과거 피드백 부스트
                var candidates = MainAiCodeIndex.SuggestRelatedPaths(
                    _solutionRoot,
                    tags,
                    boostsByTag: t => _relevance.GetBoostsForTag(t),
                    maxPaths: 8);

                // 후보가 너무 없으면 기존 방식(path/hint)로 폴백
                var first = ResolveCodePath(_solutionRoot, env?.Path, env?.Hint);
                if (first != null)
                    candidates = new[] { first }.Concat(candidates).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();

                if (candidates.Count == 0)
                {
                    pending.Text = string.IsNullOrWhiteSpace(text)
                        ? "어떤 파일을 읽어야 할지 모르겠어. 예: 'ViewModel 코드 보여줘' 또는 'javis/ViewModels/... 파일 보여줘'"
                        : text;
                    AnswerText = pending.Text;
                    return;
                }

                pending.Text = "관련 코드 후보를 확인 중…";
                AnswerText = pending.Text;

                // 후보 스니펫 묶기(짧게)
                var bundle = new System.Text.StringBuilder();
                var loaded = new List<string>();
                foreach (var c in candidates)
                {
                    var rel = ResolveCodePath(_solutionRoot, c, hint: null);
                    if (rel == null) continue;

                    try
                    {
                        var snip = ReadCodeSnippet(_solutionRoot, rel, maxChars: 4500);
                        bundle.AppendLine($"// FILE: {rel}");
                        bundle.AppendLine(snip);
                        bundle.AppendLine();
                        loaded.Add(rel);
                    }
                    catch { }
                }

                if (loaded.Count == 0)
                {
                    pending.Text = "코드를 읽지 못했어. 파일 권한/경로를 확인해줘.";
                    AnswerText = pending.Text;
                    return;
                }

                // 모델에게 "핵심 파일" 선택을 요청(자연어 아님, JSON 최소)
                string[] picked;
                try
                {
                    var pickRaw = await _help.PickRelevantFilesAsync(q, loaded, bundle.ToString(), _askCts.Token);
                    var pickJson = javis.Services.JsonUtil.ExtractFirstJsonObject(pickRaw);
                    var pickEnv = JsonSerializer.Deserialize<FilePickResult>(pickJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    picked = (pickEnv?.Pick ?? Array.Empty<string>())
                        .Select(s => (s ?? "").Trim())
                        .Where(s => s.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(3)
                        .ToArray();
                }
                catch
                {
                    picked = loaded.Take(2).ToArray();
                }

                if (picked.Length == 0)
                    picked = loaded.Take(2).ToArray();

                // 최종 분석용으로 선택 파일을 더 크게 읽기
                var combined = new System.Text.StringBuilder();
                foreach (var p in picked)
                {
                    var rel = ResolveCodePath(_solutionRoot, p, hint: null) ?? p;
                    try
                    {
                        var snip = ReadCodeSnippet(_solutionRoot, rel, maxChars: 12_000);
                        combined.AppendLine($"// FILE: {rel}");
                        combined.AppendLine(snip);
                        combined.AppendLine();
                    }
                    catch { }
                }

                pending.Text = "코드를 읽고 정리 중…";
                AnswerText = pending.Text;

                var analysis = await _help.AnalyzeWithCodeAsync(q, picked.FirstOrDefault() ?? loaded[0], combined.ToString(), _askCts.Token);
                var final2 = string.IsNullOrWhiteSpace(analysis) ? "(응답이 비어있음)" : analysis.Trim();
                pending.Text = final2;
                AnswerText = final2;

                // 이번 선택을 기억
                _lastTags = tags;
                _lastPickedFiles = picked;
                _lastAnswerAt = DateTimeOffset.UtcNow;
                _lastRewarded = false;

                return;
            }

            if (intent == "navigate")
            {
                var target = (env?.Target ?? "").Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(target))
                {
                    RequestNavigate?.Invoke(target);
                    if (string.IsNullOrWhiteSpace(text))
                        text = $"{target} 화면으로 이동할게.";
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(text))
                        text = "어느 화면으로 이동할지 모르겠어. (home/chat/todos/skills/settings)";
                }
            }
            else if (intent == "action")
            {
                var name = (env?.Name ?? "").Trim().ToLowerInvariant();
                var date = (env?.Date ?? "").Trim();
                if (string.IsNullOrWhiteSpace(date)) date = null;

                if (!string.IsNullOrWhiteSpace(name))
                {
                    RequestAction?.Invoke(name, date);
                    if (string.IsNullOrWhiteSpace(text))
                        text = $"요청을 실행할게: {name}";
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(text))
                        text = "어떤 액션을 실행할지 모르겠어.";
                }
            }

            var final = string.IsNullOrWhiteSpace(text) ? "(응답이 비어있음)" : text;
            pending.Text = final;
            AnswerText = final;

            try
            {
                if (q.Contains("업데이트", StringComparison.OrdinalIgnoreCase) ||
                    q.Contains("기능", StringComparison.OrdinalIgnoreCase) ||
                    q.Contains("어디", StringComparison.OrdinalIgnoreCase) ||
                    q.Contains("사용", StringComparison.OrdinalIgnoreCase) ||
                    q.Contains("설정", StringComparison.OrdinalIgnoreCase) ||
                    q.Contains("방법", StringComparison.OrdinalIgnoreCase))
                {
                    MainAiDocBus.PublishSuggestion($"사용자 질문(도움말): {q}", source: "help_widget");
                }
            }
            catch { }
        }
        catch (OperationCanceledException)
        {
            pending.Text = "취소됨(응답 지연 또는 네트워크 문제).";
            AnswerText = pending.Text;
        }
        catch (Exception ex)
        {
            pending.Text = "오류: " + ex.Message;
            AnswerText = pending.Text;
        }
        finally
        {
            IsBusy = false;

            try { _askCts?.Dispose(); } catch { }
            _askCts = null;
        }
    }

    private static string BuildToolFeedback(ExecutionResult ex)
    {
        if (ex.RequiresConfirmation)
            return "RequiresConfirmation: " + (ex.ConfirmationPrompt ?? "confirmation required");

        return ex.Success
            ? "Success"
            : "Failed: " + (ex.Error ?? "unknown error");
    }
}
