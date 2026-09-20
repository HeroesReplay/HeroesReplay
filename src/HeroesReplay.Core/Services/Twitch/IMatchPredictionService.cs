using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Twitch;

public interface IMatchPredictionService
{
    Task StartAsync(LoadedReplay replay, CancellationToken cancellationToken);
    Task ResolveAsync(LoadedReplay replay, CancellationToken cancellationToken);
    Task TestAsync(int? winningTeam, CancellationToken cancellationToken);
}
