using System.Collections.Generic;

namespace HeroesReplay.Core.Services.Twitch;

public sealed record PredictionParticipant(
    string UserId,
    string DisplayName,
    string Login,
    int PointsUsed,
    int PointsWon,
    bool Won,
    int Streak
);

public sealed class PredictionReport
{
    public string PredictionId { get; init; }

    public string Title { get; init; }

    public string WinningOutcome { get; init; }

    public IReadOnlyList<PredictionParticipant> Winners { get; init; } =
        new PredictionParticipant[0];

    public IReadOnlyList<PredictionParticipant> Losers { get; init; } =
        new PredictionParticipant[0];
}
