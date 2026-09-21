using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Connectivity;

public interface INetworkProbe
{
    Task<bool> ProbeInternetAsync(CancellationToken cancellationToken);
    Task<bool> ProbeTwitchAsync(CancellationToken cancellationToken);
    Task<bool> ProbeHeroesProfileAsync(CancellationToken cancellationToken);
}
