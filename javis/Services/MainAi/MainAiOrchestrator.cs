using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using javis.Services;

namespace javis.Services.MainAi;

public sealed class MainAiOrchestrator : IDisposable
{
    public static MainAiOrchestrator Instance { get; } = new();

    private const string OllamaBaseUrl = "http://localhost:11434";

    private static readonly TimeSpan MainAiWorkTimeout = TimeSpan.FromSeconds(18);

    private readonly MainAiProfileExtractor _extractor;

    private readonly ConcurrentQueue<MainAiEvent> _q = new();
    private readonly AutoResetEvent _signal = new(false);

    private CancellationTokenSource? _cts;
    private Task? _worker;

    private int _chatBusy; // 0/1

    // Debounce windows: background LLM jobs should be less aggressive when chat is busy.
    private static readonly TimeSpan IdleDebounce = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan BusyDebounce = TimeSpan.FromSeconds(75);

    private DateTimeOffset _lastUserMsgAt = DateTimeOffset.MinValue;
    private string _lastUserMsg = "";
    private string _lastProcessedUserMsg = "";
    private (string kind, DateTimeOffset at, string text) _pending = ("", DateTimeOffset.MinValue, "");

    private readonly System.Collections.Generic.Dictionary<string, DateTimeOffset> _lastKindAt =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan KindCooldown = TimeSpan.FromMinutes(2);

    private MainAiOrchestrator()
    {
        _extractor = new MainAiProfileExtractor(OllamaBaseUrl, RuntimeSettings.Instance.AiModelName);
    }

    public void Start()
    {
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        MainAiEventBus.Raised += OnEvent;

        _worker = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
    }

    private void OnEvent(MainAiEvent evt)
    {
        _q.Enqueue(evt);
        _signal.Set();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // wait for any event or periodically wake up to check debounce
            _signal.WaitOne(TimeSpan.FromMilliseconds(300));

            while (_q.TryDequeue(out var evt))
            {
                switch (evt)
                {
                    case ChatRequestStarted:
                        System.Threading.Interlocked.Exchange(ref _chatBusy, 1);
                        break;
                    case ChatRequestEnded:
                        System.Threading.Interlocked.Exchange(ref _chatBusy, 0);
                        break;
                    case ChatUserMessageObserved um:
                        _pending = ("chat.user", um.At, um.Text);
                        break;
                    case ProgramEventObserved pe:
                        _pending = (pe.Kind ?? "program", pe.At, pe.Text);
                        break;
                }
            }

            // Background work scheduling: only run when debounce window elapsed.
            var debounce = System.Threading.Interlocked.CompareExchange(ref _chatBusy, 0, 0) == 1
                ? BusyDebounce
                : IdleDebounce;

            if (_pending.at == DateTimeOffset.MinValue) continue;
            if (DateTimeOffset.Now - _pending.at < debounce) continue;

            var kind = _pending.kind;
            var msg = _pending.text;
            _pending = ("", DateTimeOffset.MinValue, "");

            try
            {
                await RunProfileExtractionAsync(kind, msg, ct);
            }
            catch
            {
                // never crash background loop
            }
        }
    }

    private async Task RunProfileExtractionAsync(string kind, string text, CancellationToken ct)
    {
        kind = (kind ?? "").Trim();
        text = (text ?? "").Trim();
        if (text.Length == 0) return;

        if (kind.Length > 0)
        {
            if (_lastKindAt.TryGetValue(kind, out var last) && DateTimeOffset.Now - last < KindCooldown)
                return;
        }

        if (string.Equals(_lastProcessedUserMsg, text, StringComparison.Ordinal))
            return;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(MainAiWorkTimeout);

        var svc = UserProfileService.Instance;
        var p = svc.TryGetActiveProfile();
        if (p == null) return;

        Dictionary<string, string> extracted;
        try
        {
            extracted = await _extractor.ExtractSafeAsync(
                lastUserMessage: BuildKindScopedInput(kind, text),
                existingReportText: p.ToReportText(includeSources: true),
                ct: timeoutCts.Token);
        }
        catch
        {
            return;
        }

        if (extracted.Count == 0) return;

        extracted = MainAiFieldPolicy.Apply(extracted);

        var merged = new System.Collections.Generic.Dictionary<string, string>(p.Fields, StringComparer.OrdinalIgnoreCase);
        var changed = new System.Collections.Generic.List<string>();

        foreach (var kv in extracted)
        {
            if (!merged.TryGetValue(kv.Key, out var cur) || string.IsNullOrWhiteSpace(cur))
            {
                merged[kv.Key] = kv.Value;
                changed.Add(kv.Key);
            }
        }

        if (changed.Count == 0) return;

        merged["main_ai_model"] = RuntimeSettings.Instance.AiModelName;
        svc.SaveProfile(p with { Fields = merged });

        try { MainAiChangeLog.Append(p.Id, kind, changed); } catch { }

        _lastProcessedUserMsg = text;
        if (kind.Length > 0) _lastKindAt[kind] = DateTimeOffset.Now;
    }

    private static string BuildKindScopedInput(string kind, string text)
    {
        kind = (kind ?? "").Trim();
        text = (text ?? "").Trim();

        if (kind.StartsWith("skill", StringComparison.OrdinalIgnoreCase))
        {
            return $"[프로그램 이벤트: 스킬 실행]\n{text}\n\n이 이벤트로부터 사용자의 관심사/목표/자주 쓰는 기능 같은 '일반 프로필'만 추출해라.";
        }

        if (kind.StartsWith("files", StringComparison.OrdinalIgnoreCase))
        {
            return $"[프로그램 이벤트: 파일 작업]\n{text}\n\n이 이벤트로부터 사용자의 작업 성향/관심 영역 같은 '일반 프로필'만 추출해라.";
        }

        // default: chat user message
        return text;
    }

    public void Dispose()
    {
        try { MainAiEventBus.Raised -= OnEvent; } catch { }
        try { _cts?.Cancel(); } catch { }
        try { _cts?.Dispose(); } catch { }
        _cts = null;
    }
}
