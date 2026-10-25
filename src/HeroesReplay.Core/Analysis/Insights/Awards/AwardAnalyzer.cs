using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Analysis.Insights.Awards;

/// <summary>
/// Awards that stand out (#512): the player has the game's award and its count is at least the
/// kind's threshold (<see cref="AwardInsightSettings"/>). The game hands one award to most players,
/// so the award alone is common; the count makes it notable. AI players are left out.
/// </summary>
public sealed class AwardAnalyzer : IInsightAnalyzer<AwardInsights>
{
    private readonly AwardInsightSettings settings;

    public AwardAnalyzer(AwardInsightSettings settings)
    {
        this.settings = settings ?? new AwardInsightSettings();
    }

    public AwardInsights Analyze(ReplaySnapshot snapshot)
    {
        var awards = new List<PlayerAward>();
        foreach (
            SnapshotPlayer player in (snapshot?.Players ?? new List<SnapshotPlayer>()).Where(
                player => !player.IsAi && player.HeroName != null
            )
        )
        {
            Add(
                awards,
                player,
                AwardKind.HatTrick,
                "HatTrick",
                player.Score.Kills,
                settings.HatTrick ? 1 : 0
            );
            Add(
                awards,
                player,
                AwardKind.ClutchHealer,
                "ClutchHealer",
                player.Score.ClutchHeals,
                settings.ClutchHeals
            );
            Add(
                awards,
                player,
                AwardKind.Avenger,
                "MostVengeancesPerformed",
                player.Score.Vengeances,
                settings.Vengeances
            );
            Add(
                awards,
                player,
                AwardKind.EscapeArtist,
                "MostEscapes",
                player.Score.Escapes,
                settings.Escapes
            );
        }

        return new AwardInsights
        {
            Awards = awards
                .OrderBy(award => award.Kind)
                .ThenByDescending(award => award.Value)
                .ToList(),
        };
    }

    private static void Add(
        List<PlayerAward> awards,
        SnapshotPlayer player,
        AwardKind kind,
        string award,
        int value,
        int threshold
    )
    {
        if (threshold <= 0 || value < threshold || !player.Score.HasAward(award))
        {
            return;
        }

        awards.Add(
            new PlayerAward
            {
                PlayerIndex = player.Index,
                Team = player.Team,
                Hero = player.HeroName,
                Kind = kind,
                Value = value,
            }
        );
    }
}
