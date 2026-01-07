using System;
using System.Threading;
using System.Threading.Tasks;

namespace javis.Services.Solo;

public sealed class ChatPageSoloBackendAdapter : ISoloBackend
{
    private readonly Func<string, CancellationToken, Task> _runTurn;

    public ChatPageSoloBackendAdapter(Func<string, CancellationToken, Task> runTurn)
    {
        _runTurn = runTurn;
    }

    public async Task<string?> RunSoloTurnAsync(SoloTurnInput input, CancellationToken ct)
    {
        await _runTurn(input.UserText, ct);
        return null;
    }
}
