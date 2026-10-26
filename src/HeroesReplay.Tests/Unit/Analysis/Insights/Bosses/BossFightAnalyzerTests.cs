using System;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Bosses;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis.Insights.Bosses;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class BossFightAnalyzerTests
{
    /// <summary>A pit in Blue's half: nearer Blue's core (40, 110) than Red's (210, 110).</summary>
    private static readonly MapPoint BluePit = new(90, 170);

    private static readonly MapPoint Away = new(160, 60);

    [Fact]
    public void BlueTakingItsBoss_RedCapturesIt_IsASteal_NamedAfterTheBossKiller()
    {
        ScriptedPositions positions = Blue(4, from: 100, to: 130)
            .Path(5, time => Seconds(time) >= 126 ? BluePit : Away);

        BossFight fight = Single(positions, Capture(130, team: 1, killer: 5));

        Assert.Equal(BossFightKind.Steal, fight.Kind);
        Assert.Equal(0, fight.Starter);
        Assert.Equal(1, fight.Team);
        Assert.Equal(0, fight.Side);
        Assert.Equal("Li-Ming", fight.KillerHero);
        Assert.Equal(100, fight.StartSecond);
        Assert.Equal(31, fight.Teams[0].GroupSeconds);
        Assert.Equal(4, fight.Teams[0].MostHeroes);
        Assert.Equal(126, fight.Teams[1].FirstHeroSecond);
    }

    [Fact]
    public void BlueKillsTheBoss_RedCapturesItsCircle_IsASteal_WhereverTheHeroesStood()
    {
        // Sky Temple 65268844: Red's unit killed the boss at 14:01, Blue captured it at 14:06.
        ScriptedPositions positions = Blue(4, from: 125, to: 130)
            .Path(5, time => Seconds(time) >= 124 ? BluePit : Away);

        BossFight fight = Single(positions, Capture(130, team: 1, killer: 0));

        Assert.Equal(BossFightKind.Steal, fight.Kind);
        Assert.True(fight.KilledByOtherTeam);
        Assert.Equal(0, fight.KillerTeam);
        Assert.Equal("Muradin", fight.KillerHero);
    }

    [Fact]
    public void RedLastHitsTheBossBlueStarted_BlueStillCapturesIt_IsDefended()
    {
        // The corpus's 15:47: Blue on the boss from 15:23, a lone Red hero landed the blow at the
        // end, and Blue took the circle. Red's steal failed.
        ScriptedPositions positions = Blue(4, from: 100, to: 130)
            .Path(5, time => Seconds(time) >= 127 ? BluePit : Away);

        BossFight fight = Single(positions, Capture(130, team: 0, killer: 5));

        Assert.Equal(BossFightKind.Defended, fight.Kind);
        Assert.True(fight.KilledByOtherTeam);
    }

    [Fact]
    public void RedThereWithinSecondsOfBlue_IsNotASteal()
    {
        ScriptedPositions positions = Blue(4, from: 100, to: 130)
            .Path(5, time => Seconds(time) >= 103 ? BluePit : Away);

        Assert.Equal(BossFightKind.Take, Single(positions, Capture(130, team: 1, killer: 5)).Kind);
    }

    [Fact]
    public void BlueGroupedOnlyBriefly_IsNotASteal()
    {
        ScriptedPositions positions = Blue(4, from: 124, to: 130)
            .Path(5, time => Seconds(time) >= 130 ? BluePit : Away);

        Assert.Equal(BossFightKind.Take, Single(positions, Capture(130, team: 1, killer: 5)).Kind);
    }

    [Fact]
    public void BlueKeepsItsBossThroughAFightWithDeaths_IsDefended()
    {
        ScriptedPositions positions = Blue(4, from: 100, to: 130);
        for (int red = 5; red < 8; red++)
        {
            positions.Path(red, time => Seconds(time) >= 115 ? BluePit : Away);
        }

        ReplaySnapshot snapshot = Snapshot(positions, Capture(130, team: 0, killer: 0)) with
        {
            HeroDeaths = new[]
            {
                Death(5, 1, 120, BluePit),
                Death(6, 1, 124, new MapPoint(95, 175)),
                Death(1, 0, 127, BluePit),
                // Far from the pit: not part of the fight.
                Death(7, 1, 125, Away),
            },
        };

        BossFight fight = Assert.Single(
            new BossFightAnalyzer(new BossFightInsightSettings()).Analyze(snapshot).Fights
        );

        Assert.Equal(BossFightKind.Defended, fight.Kind);
        Assert.Equal(0, fight.Starter);
        Assert.Equal(16, fight.TogetherSeconds);
        Assert.Equal(1, fight.Teams[0].Deaths);
        Assert.Equal(2, fight.Teams[1].Deaths);
    }

    [Fact]
    public void AnUncontestedTake_IsATake()
    {
        BossFight fight = Single(Blue(4, from: 100, to: 130), Capture(130, team: 0, killer: 0));

        Assert.Equal(BossFightKind.Take, fight.Kind);
        Assert.Null(fight.Teams[1].FirstHeroSecond);
    }

    [Fact]
    public void Aram_HasNoBossFights()
    {
        ReplaySnapshot snapshot = Snapshot(
            Blue(4, from: 100, to: 130),
            Capture(130, team: 1, killer: 5)
        ) with
        {
            Aram = true,
        };

        Assert.Empty(new BossFightAnalyzer(null).Analyze(snapshot).Fights);
    }

    private static BossFight Single(ScriptedPositions positions, BossCapture capture) =>
        Assert.Single(
            new BossFightAnalyzer(new BossFightInsightSettings())
                .Analyze(Snapshot(positions, capture))
                .Fights
        );

    private static ReplaySnapshot Snapshot(ScriptedPositions positions, BossCapture capture) =>
        InsightFixtures.Snapshot(
            new[]
            {
                InsightFixtures.Player(0, 0, "Muradin"),
                InsightFixtures.Player(1, 0, "Valla"),
                InsightFixtures.Player(2, 0, "Rehgar"),
                InsightFixtures.Player(3, 0, "Sonya"),
                InsightFixtures.Player(4, 0, "Abathur"),
                InsightFixtures.Player(5, 1, "Li-Ming"),
                InsightFixtures.Player(6, 1, "Johanna"),
                InsightFixtures.Player(7, 1, "Anduin"),
                InsightFixtures.Player(8, 1, "Raynor"),
                InsightFixtures.Player(9, 1, "Thrall"),
            },
            positions
        ) with
        {
            BossCaptures = new[] { capture },
        };

    /// <summary>Blue players 0 to <paramref name="count"/> - 1 at the pit from one second to another.</summary>
    private static ScriptedPositions Blue(int count, int from, int to)
    {
        var positions = new ScriptedPositions();
        for (int player = 0; player < count; player++)
        {
            positions.Path(
                player,
                time => Seconds(time) >= from && Seconds(time) <= to ? BluePit : Away
            );
        }

        return positions;
    }

    private static BossCapture Capture(int second, int team, int killer) =>
        new(1, team, TimeSpan.FromSeconds(second), BluePit, killer);

    private static HeroDeath Death(int player, int team, int second, MapPoint point) =>
        new(player, team, TimeSpan.FromSeconds(second), point);

    private static int Seconds(TimeSpan time) => (int)time.TotalSeconds;
}
