using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.BSteps;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis.Insights.BSteps;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class BStepAnalyzerTests
{
    private static readonly MapPoint Fight = new(100, 100);

    [Fact]
    public void FastHearthCancels_AfterTheBstepperKilledSomeone_AreABstepWithThatDeath()
    {
        // 65268641: Sonya killed Muradin at 13:09, then hearth-cancelled 4 times a second.
        ReplaySnapshot snapshot = Snapshot(
            Cycles(player: 0, from: 790, count: 5, gap: 0.25, cancel: 0.13),
            new[] { new HeroDeath(5, 1, TimeSpan.FromSeconds(789.4), Fight, KillerIndex: 0) }
        );

        BStep step = Assert.Single(new BStepAnalyzer(null).Analyze(snapshot).BSteps);

        Assert.Equal(0, step.PlayerIndex);
        Assert.Equal("Sonya", step.Hero);
        Assert.Equal(5, step.Cycles);
        Assert.Equal(790, step.StartSecond);
        Assert.Equal(791, step.EndSecond);
        Assert.Equal(0.25, step.MedianGapSeconds, 2);
        BStepDeath death = Assert.Single(step.Deaths);
        Assert.Equal(BStepDeathKind.KilledByBstepper, death.Kind);
        Assert.Equal("Muradin", death.Hero);
        Assert.False(death.After);
        Assert.Equal(789, death.Second);
    }

    [Fact]
    public void TheBstepperDyingSoonAfter_IsLinked_WhereverItHappened()
    {
        ReplaySnapshot snapshot = Snapshot(
            Cycles(player: 0, from: 360, count: 6, gap: 0.2, cancel: 0.1),
            new[] { new HeroDeath(0, 0, TimeSpan.FromSeconds(372), new MapPoint(10, 10), 5) }
        );

        BStepDeath death = Assert.Single(
            Assert.Single(new BStepAnalyzer(null).Analyze(snapshot).BSteps).Deaths
        );

        Assert.Equal(BStepDeathKind.Bstepper, death.Kind);
        Assert.True(death.After);
    }

    [Fact]
    public void ADeathNextToTheBstepper_IsLinked_OneFarAwayOrLongBefore_IsNot()
    {
        var positions = new ScriptedPositions().At(0, Fight);
        ReplaySnapshot snapshot = Snapshot(
            Cycles(player: 0, from: 600, count: 4, gap: 0.3, cancel: 0.2),
            new[]
            {
                new HeroDeath(6, 1, TimeSpan.FromSeconds(595), new MapPoint(108, 104), 2),
                new HeroDeath(1, 0, TimeSpan.FromSeconds(606), new MapPoint(95, 98), 7),
                new HeroDeath(7, 1, TimeSpan.FromSeconds(598), new MapPoint(160, 40), 3),
                new HeroDeath(8, 1, TimeSpan.FromSeconds(570), Fight, 3),
            },
            positions
        );

        BStep step = Assert.Single(new BStepAnalyzer(null).Analyze(snapshot).BSteps);

        Assert.Equal(
            new[] { (6, BStepDeathKind.Enemy, false), (1, BStepDeathKind.Ally, true) },
            step.Deaths.Select(death => (death.PlayerIndex, death.Kind, death.After))
        );
    }

    [Fact]
    public void ThreeCancels_SlowCancels_SlowGaps_OrAbilityCancels_AreNotBstepping()
    {
        var casts = new List<HearthCast>();
        casts.AddRange(Cycles(player: 0, from: 100, count: 3, gap: 0.2, cancel: 0.1));
        // A hero hearths, waits, then walks off: one cancel, seconds in.
        casts.AddRange(Cycles(player: 1, from: 200, count: 4, gap: 0.3, cancel: 2.5));
        // 65269682: Rexxar's hearths cancelled seconds apart.
        casts.AddRange(Cycles(player: 2, from: 300, count: 5, gap: 2.0, cancel: 0.3));
        // Cancelled by abilities, not moves.
        casts.AddRange(
            Enumerable.Range(0, 6).Select(i => new HearthCast(3, 400 + (i * 0.2), null))
        );

        Assert.Empty(new BStepAnalyzer(null).Analyze(Snapshot(casts)).BSteps);
    }

    [Fact]
    public void TwoBstepsByOnePlayer_ASecondApartOrMore_AreTwo_AndAiIsLeftOut()
    {
        var casts = new List<HearthCast>();
        casts.AddRange(Cycles(player: 0, from: 100, count: 4, gap: 0.2, cancel: 0.1));
        casts.AddRange(Cycles(player: 0, from: 103, count: 4, gap: 0.2, cancel: 0.1));
        casts.AddRange(Cycles(player: 9, from: 100, count: 8, gap: 0.2, cancel: 0.1));

        IReadOnlyList<BStep> steps = new BStepAnalyzer(null).Analyze(Snapshot(casts)).BSteps;

        Assert.Equal(new[] { 100, 103 }, steps.Select(step => step.StartSecond));
        Assert.All(steps, step => Assert.Empty(step.Deaths));
    }

    [Fact]
    public void Snapshot_ReadsEachHearthWithTheMoveThatCancelledIt()
    {
        var bstepper = new Player { Team = 0 };
        var other = new Player { Team = 1 };
        var replay = new Replay
        {
            ReplayBuild = 98304,
            Players = new[] { bstepper, other },
            GameEvents = new List<GameEvent>
            {
                Order(bstepper, 1600, Ability(115)),
                Order(other, 1601, Ability(115)),
                Order(bstepper, 1602, Move()),
                Order(bstepper, 1604, Ability(115)),
                new()
                {
                    eventType = GameEventType.CCmdUpdateTargetPointEvent,
                    player = bstepper,
                    ticksElapsed = 1606,
                },
                Order(bstepper, 1608, Ability(115)),
                Order(bstepper, 1610, Ability(40)),
                Order(other, 1640, Move()),
                Order(bstepper, 1650, Ability(115)),
            },
        };

        ReplaySnapshot snapshot = ReplaySnapshotReader.Read(
            replay,
            Array.Empty<Hero>(),
            new InsightsSettings()
        );

        Assert.Equal(
            new (int, double, double?)[]
            {
                (0, 100, 100.125),
                (1, 100.0625, 102.5),
                (0, 100.25, 100.375),
                (0, 100.5, null),
                (0, 103.125, null),
            },
            snapshot.HearthCasts.Select(cast => (cast.PlayerIndex, cast.Seconds, cast.CancelSeconds))
        );
    }

    [Fact]
    public void HearthLink_FollowsTheAbilityDetectionTable()
    {
        var settings = new BStepInsightSettings();

        Assert.Equal(200, settings.HearthLink(50000));
        Assert.Equal(114, settings.HearthLink(79032));
        Assert.Equal(115, settings.HearthLink(98304));
    }

    private static IEnumerable<HearthCast> Cycles(
        int player,
        double from,
        int count,
        double gap,
        double cancel
    ) =>
        Enumerable
            .Range(0, count)
            .Select(i => new HearthCast(player, from + (i * gap), from + (i * gap) + cancel));

    private static ReplaySnapshot Snapshot(
        IEnumerable<HearthCast> casts,
        IReadOnlyList<HeroDeath> deaths = null,
        IHeroPositions positions = null
    ) =>
        InsightFixtures.Snapshot(
            new[]
            {
                InsightFixtures.Player(0, 0, "Sonya"),
                InsightFixtures.Player(1, 0, "Anduin"),
                InsightFixtures.Player(2, 0, "Rexxar"),
                InsightFixtures.Player(3, 0, "Tyrael"),
                InsightFixtures.Player(5, 1, "Muradin"),
                InsightFixtures.Player(6, 1, "Genji"),
                InsightFixtures.Player(7, 1, "Hanzo"),
                InsightFixtures.Player(8, 1, "Ana"),
                InsightFixtures.Player(9, 1, "Murky", ai: true),
            },
            positions
        ) with
        {
            HearthCasts = casts.OrderBy(cast => cast.Seconds).ToArray(),
            HeroDeaths = deaths ?? Array.Empty<HeroDeath>(),
        };

    private static GameEvent Order(Player player, int ticks, TrackerEventStructure ability) =>
        new()
        {
            eventType = GameEventType.CCmdEvent,
            player = player,
            ticksElapsed = ticks,
            data = new TrackerEventStructure
            {
                array = new[] { new TrackerEventStructure(), ability, null, null, null },
            },
        };

    private static TrackerEventStructure Ability(int link) =>
        new()
        {
            array = new[]
            {
                new TrackerEventStructure { unsignedInt = (ulong)link },
                new TrackerEventStructure { unsignedInt = 0 },
                new TrackerEventStructure(),
            },
        };

    private static TrackerEventStructure Move() => null;
}
