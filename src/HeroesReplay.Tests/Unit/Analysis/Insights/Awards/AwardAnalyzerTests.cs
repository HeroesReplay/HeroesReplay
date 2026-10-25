using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Awards;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis.Insights.Awards;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class AwardAnalyzerTests
{
    [Fact]
    public void AnAwardStandsOut_OnlyWithItsCountPastTheThreshold()
    {
        AwardInsights insights = new AwardAnalyzer(new AwardInsightSettings()).Analyze(
            InsightFixtures.Snapshot(
                new[]
                {
                    // The award and 9 clutch heals: a standout.
                    Player(
                        0,
                        "Rehgar",
                        new PlayerScore { ClutchHeals = 9, Awards = new[] { "ClutchHealer" } }
                    ),
                    // The award with too few.
                    Player(
                        1,
                        "Anduin",
                        new PlayerScore { ClutchHeals = 5, Awards = new[] { "ClutchHealer" } }
                    ),
                    // Many escapes without the award.
                    Player(2, "Tracer", new PlayerScore { Escapes = 9, Awards = new[] { "MVP" } }),
                    Player(
                        5,
                        "Genji",
                        new PlayerScore
                        {
                            Vengeances = 6,
                            Awards = new[] { "MostVengeancesPerformed" },
                        }
                    ),
                    Player(
                        6,
                        "Valla",
                        new PlayerScore { Escapes = 7, Awards = new[] { "MostEscapes" } }
                    ),
                    // Hat Trick stands out on its own; its value is the kills.
                    Player(
                        7,
                        "Falstad",
                        new PlayerScore { Kills = 8, Awards = new[] { "HatTrick" } }
                    ),
                }
            )
        );

        Assert.Equal(
            new[]
            {
                (AwardKind.HatTrick, "Falstad", 8),
                (AwardKind.ClutchHealer, "Rehgar", 9),
                (AwardKind.Avenger, "Genji", 6),
                (AwardKind.EscapeArtist, "Valla", 7),
            },
            System.Linq.Enumerable.Select(
                insights.Awards,
                award => (award.Kind, award.Hero, award.Value)
            )
        );
    }

    [Fact]
    public void AZeroThreshold_TurnsTheKindOff()
    {
        AwardInsights insights = new AwardAnalyzer(
            new AwardInsightSettings { ClutchHeals = 0 }
        ).Analyze(
            InsightFixtures.Snapshot(
                new[]
                {
                    Player(
                        0,
                        "Rehgar",
                        new PlayerScore { ClutchHeals = 12, Awards = new[] { "ClutchHealer" } }
                    ),
                }
            )
        );

        Assert.Empty(insights.Awards);
    }

    private static SnapshotPlayer Player(int index, string hero, PlayerScore score) =>
        InsightFixtures.Player(index, index < 5 ? 0 : 1, hero, score);
}
