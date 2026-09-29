using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Twitch;

public interface IMatchPredictionService
{
    Task StartAsync(LoadedReplay replay, CancellationToken cancellationToken);
    Task<bool> OpenAsync(int replayId, string map, CancellationToken cancellationToken);
    Task ResolveAsync(LoadedReplay replay, CancellationToken cancellationToken);
    Task ResolveTeamAsync(int? winningTeam, CancellationToken cancellationToken);
    Task ResolveReplayAsync(int replayId, int? winningTeam, CancellationToken cancellationToken);
    Task<PredictionReconcileResult> ReconcileAsync(CancellationToken cancellationToken);
    Task RetryPendingAsync(CancellationToken cancellationToken);
    Task TestAsync(int? winningTeam, CancellationToken cancellationToken);
}
