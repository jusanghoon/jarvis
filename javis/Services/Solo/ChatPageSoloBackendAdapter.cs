using System;
using System.Threading;
using System.Threading.Tasks;

namespace javis.Services.Solo;

public sealed class ChatPageSoloBackendAdapter : ISoloBackend, ISoloTokenStreamSource
{
    private readonly Func<string, CancellationToken, Task> _runTurn;

    public event Action<string>? OnTokenReceived;

    public ChatPageSoloBackendAdapter(Func<string, CancellationToken, Task> runTurn)
    {
        _runTurn = runTurn;
    }

    public void EmitToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        try { OnTokenReceived?.Invoke(token); } catch { }
    }

    public async Task<string?> RunSoloTurnAsync(SoloTurnInput input, CancellationToken ct)
    {
        await _runTurn(input.UserText, ct);
        return null;
    }
}
