using System.Collections.Generic;

namespace HeroesReplay.Core.Twitch.Predictions;

public sealed record PredictionParticipant(
    string UserId,
    string DisplayName,
    string Login,
    int PointsUsed,
    int PointsWon,
    bool Won,
    int Streak
);

public sealed record PredictionReport
{
    public string PredictionId { get; init; }

    public string Title { get; init; }

    public string WinningOutcome { get; init; }

    /// <summary>
    /// Blue/Red's other team. For a rating prediction, the wrong rating most points went on;
    /// the loser counts below then cover every wrong rating (#411).
    /// </summary>
    public string LosingOutcome { get; init; }

    /// <summary>Blue/Red, or the Blind Rating guess (#411). Saved reports without it are Blue/Red.</summary>
    public PredictionKind Kind { get; init; }

    /// <summary>The English catalog map name, or null when the map is not a catalog map.</summary>
    public string Map { get; init; }

    /// <summary>Viewers on the winning outcome (Helix <c>users</c>).</summary>
    public int WinnerVoters { get; init; }

    /// <summary>Viewers on the losing outcome (Helix <c>users</c>).</summary>
    public int LoserVoters { get; init; }

    /// <summary>Channel points staked on the winning outcome (Helix <c>channel_points</c>).</summary>
    public int WinnerPoints { get; init; }

    /// <summary>Channel points staked on the losing outcome (Helix <c>channel_points</c>).</summary>
    public int LoserPoints { get; init; }

    /// <summary>
    /// The <see cref="PredictionVerdict"/> line, chosen once when the prediction resolved so chat
    /// and every render of the page (<c>obs pages</c> too) say the same thing.
    /// </summary>
    public string Verdict { get; init; }

    public IReadOnlyList<PredictionParticipant> Winners { get; set; } =
        new PredictionParticipant[0];

    public IReadOnlyList<PredictionParticipant> Losers { get; set; } = new PredictionParticipant[0];
}
