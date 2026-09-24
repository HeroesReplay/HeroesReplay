using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.SelfUpdate;

public interface IReleaseUpdateGate
{
    /// <summary>
    /// Download a newer GitHub Release and start the helper that replaces this install
    /// after the process exits. Returns true when the spectator should stop.
    /// </summary>
    Task<bool> TryStageAsync(CancellationToken cancellationToken);
}
