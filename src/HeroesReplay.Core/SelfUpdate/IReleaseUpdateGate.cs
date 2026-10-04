using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.SelfUpdate;

public interface IReleaseUpdateGate
{
    /// <summary>
    /// Download and prepare a newer GitHub Release. Returns true when one is staged. Nothing
    /// stops yet: the spectator finishes the report on the waiting scene, then calls
    /// <see cref="HandOff"/>.
    /// </summary>
    Task<bool> TryStageAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Start the helper that replaces this install with the staged release after the process
    /// exits, then ask every service to stop. Returns true when the services stop.
    /// </summary>
    bool HandOff();
}
