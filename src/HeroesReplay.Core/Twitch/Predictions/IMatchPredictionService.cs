using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Replays;

namespace HeroesReplay.Core.Twitch.Predictions;

public interface IMatchPredictionService
{
    Task StartAsync(LoadedReplay replay, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the replay's prediction: Blue/Red, or the rating guess for a Blind Rating replay
    /// (<paramref name="kind"/>, #411).
    /// </summary>
    Task<bool> OpenAsync(
        int replayId,
        string map,
        PredictionKind kind,
        CancellationToken cancellationToken
    );
    Task ResolveAsync(LoadedReplay replay, CancellationToken cancellationToken);
    Task ResolveTeamAsync(int? winningTeam, CancellationToken cancellationToken);
    Task ResolveReplayAsync(int replayId, int? winningTeam, CancellationToken cancellationToken);
    Task<PredictionReconcileResult> ReconcileAsync(CancellationToken cancellationToken);
    Task RetryPendingAsync(CancellationToken cancellationToken);
    Task TestAsync(int? winningTeam, CancellationToken cancellationToken);
}
