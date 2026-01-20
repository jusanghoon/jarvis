using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using javis.ViewModels;

namespace javis.Services.Solo;

public interface ISoloTokenStreamSource
{
    event Action<string>? OnTokenReceived;
}

public sealed class SoloOrchestrator : IAsyncDisposable
{
    public enum SoloState { Off, AwaitingStart, Running }

    private readonly ISoloUiSink _ui;
    private readonly ISoloBackend _backend;

    public event Action<string>? OnTokenReceived;

    public string ModelName { get; set; } = javis.Services.RuntimeSettings.Instance.AiModelName;

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

    private static readonly Regex FeatProposal = new(@"\[FEAT_PROPOSAL\]\s*:\s*(?<t>.+)$", RegexOptions.Compiled | RegexOptions.Multiline);

    public bool IsRunning => _loopTask is { IsCompleted: false };
    public SoloState State { get; private set; } = SoloState.Off;

    public bool AutoContinue { get; set; } = false;

    public SoloOrchestrator(ISoloUiSink ui, ISoloBackend backend)
    {
        _ui = ui;
        _backend = backend;

        if (_backend is ISoloTokenStreamSource src)
        {
            src.OnTokenReceived += t =>
            {
                try { OnTokenReceived?.Invoke(t); } catch { }
            };
        }
    }

    public async Task StartAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsRunning) return;

            try
            {
                javis.Services.Inbox.DailyInbox.Append(javis.Services.Inbox.InboxKinds.SoloRequest, new
                {
                    evt = "SoloRequestStarted",
                    at = DateTimeOffset.Now,
                    room = "solo"
                });
            }
            catch { }

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

            try
            {
                javis.Services.Inbox.DailyInbox.Append(javis.Services.Inbox.InboxKinds.SoloRequest, new
                {
                    evt = "SoloRequestEnded",
                    at = DateTimeOffset.Now,
                    room = "solo"
                });
            }
            catch { }
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
                {
                    TryHandleSoloProposal(reply);
                    _ui.PostAssistant(reply);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _ui.PostSystem($"SOLO 오류: {ex.Message}");
            }
            finally
            {
                if (AutoContinue && State == SoloState.Running && !ct.IsCancellationRequested)
                {
                    var cancelled = false;
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }

                    if (!cancelled && !ct.IsCancellationRequested && AutoContinue && State == SoloState.Running)
                    {
                        var nextId = System.Threading.Interlocked.Increment(ref _lastProcessedUserMsgId);
                        _inbox.Writer.TryWrite(new SoloTurnInput(nextId, string.Empty));
                    }
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }

    private static void TryHandleSoloProposal(string reply)
    {
        try
        {
            var m = FeatProposal.Match(reply ?? string.Empty);
            if (!m.Success) return;

            var text = (m.Groups["t"].Value ?? string.Empty).Trim();
            if (text.Length == 0) return;

            _ = UpdatesViewModel.Instance.AddUpdateAsync(text);

            try
            {
                javis.App.Kernel?.Logger?.LogText("solo.self_reflection", $"자아 성찰 결과: {text}");
            }
            catch { }
        }
        catch
        {
            // ignore
        }
    }
}
