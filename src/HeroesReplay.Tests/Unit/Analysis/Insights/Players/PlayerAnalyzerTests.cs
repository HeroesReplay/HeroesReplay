using System.Linq;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Players;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis.Insights.Players;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PlayerAnalyzerTests
{
    [Fact]
    public void Standouts_ArePositiveAndRankedDeathlessFirst()
    {
        PlayerInsights insights = Analyze(
            InsightFixtures.Player(
                0,
                0,
                "Valla",
                new PlayerScore
                {
                    Kills = 12,
                    Deaths = 1,
                    Assists = 4,
                    HeroDamage = 60000,
                }
            ),
            InsightFixtures.Player(
                1,
                0,
                "Li-Ming",
                new PlayerScore
                {
                    Kills = 8,
                    Deaths = 0,
                    Assists = 9,
                    HeroDamage = 40000,
                    Mvp = true,
                }
            ),
            InsightFixtures.Player(
                2,
                0,
                "Muradin",
                new PlayerScore
                {
                    Kills = 1,
                    Deaths = 14,
                    HeroDamage = 0,
                }
            ),
            InsightFixtures.Player(5, 1, "Genji", new PlayerScore { Kills = 9, Deaths = 4 }),
            InsightFixtures.Player(6, 1, "Uther", new PlayerScore { Kills = 2, Deaths = 5 }),
            InsightFixtures.Player(
                3,
                0,
                "AI Raynor",
                new PlayerScore { Kills = 20, Deaths = 0 },
                ai: true
            )
        );

        Assert.Equal(
            new[] { (StandoutKind.DeathlessCarry, "Li-Ming"), (StandoutKind.KillLeader, "Valla") },
            insights.Standouts.Take(2).Select(standout => (standout.Kind, standout.Player.Hero))
        );
        Standout teamKills = insights.Standouts[2];
        Assert.Equal(StandoutKind.TeamKillLeader, teamKills.Kind);
        Assert.Equal("Genji", teamKills.Player.Hero);
        Assert.DoesNotContain(
            insights.Standouts,
            standout => standout.Player.IsAi || standout.Player.Hero == "Muradin"
        );
        Assert.Equal("Li-Ming", insights.Mvp.Hero);
        Assert.Equal("8/0/9", insights.Mvp.Kda);
        Assert.Equal(0.6, insights.Lines[0].HeroDamageShare, 3);
    }

    private static PlayerInsights Analyze(params SnapshotPlayer[] players) =>
        new PlayerAnalyzer(new StandoutSettings()).Analyze(InsightFixtures.Snapshot(players));
}
