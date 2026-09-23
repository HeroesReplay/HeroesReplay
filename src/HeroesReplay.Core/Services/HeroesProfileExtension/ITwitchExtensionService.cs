using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfileExtension;

public interface ITwitchExtensionService
{
    Task<ExtensionPostOutcome> PostSnapshotAsync(
        string gameId,
        int seq,
        ExtensionSnapshot snapshot,
        CancellationToken token = default
    );

    Task<ExtensionWhoAmI> WhoAmIAsync(CancellationToken token = default);
}
