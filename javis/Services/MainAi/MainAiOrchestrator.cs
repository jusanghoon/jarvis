using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using javis.Services;

namespace javis.Services.MainAi;

public sealed class MainAiOrchestrator : IDisposable
{
    public static MainAiOrchestrator Instance { get; } = new();

    private const string MainAiModel = "qwen3:4b";

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

    private MainAiOrchestrator()
    {
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
                        _lastUserMsgAt = um.At;
                        _lastUserMsg = um.Text;
                        break;
                }
            }

            // Background work scheduling: only run when debounce window elapsed.
            var debounce = System.Threading.Interlocked.CompareExchange(ref _chatBusy, 0, 0) == 1
                ? BusyDebounce
                : IdleDebounce;

            if (_lastUserMsgAt == DateTimeOffset.MinValue) continue;
            if (DateTimeOffset.Now - _lastUserMsgAt < debounce) continue;

            // run one background job at a time
            var msg = _lastUserMsg;
            _lastUserMsgAt = DateTimeOffset.MinValue;
            _lastUserMsg = "";

            try
            {
                await RunProfileExtractionAsync(msg, ct);
            }
            catch
            {
                // never crash background loop
            }
        }
    }

    private static async Task RunProfileExtractionAsync(string lastUserMsg, CancellationToken ct)
    {
        // Placeholder for LLM-backed extraction.
        // For now, we only record the observation into fields as "최근 발화" to prove pipeline.
        // (LLM integration will be added next)

        await Task.Yield();
        ct.ThrowIfCancellationRequested();

        var svc = UserProfileService.Instance;
        var p = svc.TryGetActiveProfile();
        if (p == null) return;

        var fields = new System.Collections.Generic.Dictionary<string, string>(p.Fields, StringComparer.OrdinalIgnoreCase)
        {
            ["최근 발화"] = lastUserMsg,
            ["main_ai_model"] = MainAiModel
        };

        svc.SaveProfile(p with { Fields = fields });
    }

    public void Dispose()
    {
        try { MainAiEventBus.Raised -= OnEvent; } catch { }
        try { _cts?.Cancel(); } catch { }
        try { _cts?.Dispose(); } catch { }
        _cts = null;
    }
}
