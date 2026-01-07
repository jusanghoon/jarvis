using System.Threading;
using System.Threading.Tasks;

namespace javis.Services.Solo;

public interface ISoloBackend
{
    Task<string?> RunSoloTurnAsync(SoloTurnInput input, CancellationToken ct);
}
