using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace javis.Services.Solo;

public sealed class SoloOrchestrator : IAsyncDisposable
{
    public enum SoloState { Off, AwaitingStart, Running }

    private readonly ISoloUiSink _ui;
    private readonly ISoloBackend _backend;

    private readonly Channel<SoloTurnInput> _inbox = Channel.CreateUnbounded<SoloTurnInput>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private long _lastProcessedUserMsgId = -1;

    public bool IsRunning => _loopTask is { IsCompleted: false };
    public SoloState State { get; private set; } = SoloState.Off;

    public SoloOrchestrator(ISoloUiSink ui, ISoloBackend backend)
    {
        _ui = ui;
        _backend = backend;
    }

    public async Task StartAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));

            State = SoloState.AwaitingStart;
            _ui.PostSystem("SOLO ON");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            State = SoloState.Off;

            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            if (_loopTask != null)
            {
                try { await _loopTask.ConfigureAwait(false); } catch { }
                _loopTask = null;
            }

            _ui.PostSystem("SOLO OFF");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void ResetLastSeen(long latestUserMsgId)
    {
        _lastProcessedUserMsgId = latestUserMsgId;
    }

    public void OnUserMessage(long msgId, string text)
    {
        if (State == SoloState.Off) return;

        if (State == SoloState.AwaitingStart)
        {
            State = SoloState.Running;
            _ui.PostSystem("SOLO RUNNING");
        }

        _inbox.Writer.TryWrite(new SoloTurnInput(msgId, text));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            SoloTurnInput input;
            try
            {
                input = await _inbox.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (input.UserMsgId <= _lastProcessedUserMsgId)
            {
                _ui.PostDebug($"[solo] skip duplicate msgId={input.UserMsgId}");
                continue;
            }

            _lastProcessedUserMsgId = input.UserMsgId;

            try
            {
                var reply = await _backend.RunSoloTurnAsync(input, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(reply))
                    _ui.PostAssistant(reply);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _ui.PostSystem($"SOLO 오류: {ex.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }
}
