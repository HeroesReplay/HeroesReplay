using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Comeback;
using HeroesReplay.Core.Analysis.Insights.Finish;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using HeroesReplay.Core.Analysis.Insights.Structures;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis.Insights.Finish;

/// <summary>How the match ended: buildings lost, a core race, an insane comeback. None names a winner.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class FinishAndComebackTests
{
    private static readonly TimeSpan End = TimeSpan.FromMinutes(20);

    [Fact]
    public void Structures_CountFortsAndKeepsPerTeam()
    {
        StructureInsights structures = new StructureAnalyzer().Analyze(
            Ended(redKeeps: 2, blueKeeps: 1, blueForts: 2)
        );

        Assert.Equal((2, 1), (structures.Teams[0].FortsLost, structures.Teams[0].KeepsLost));
        Assert.Equal((0, 2), (structures.Teams[1].FortsLost, structures.Teams[1].KeepsLost));
        Assert.Equal(TimeSpan.FromMinutes(18), structures.Teams[1].LastLoss);
    }

    [Fact]
    public void CoreRace_NeedsKeepsLostOnBothSidesAndAttackersAtTheOtherCore()
    {
        var atBlueCore = new ScriptedPositions()
            .At(5, InsightFixtures.BlueCore)
            .At(6, new MapPoint(50, 115))
            .At(7, new MapPoint(150, 110));

        FinishInsights race = new FinishAnalyzer(new FinishInsightSettings()).Analyze(
            Ended(redKeeps: 2, blueKeeps: 1, positions: atBlueCore)
        );
        FinishInsights oneSided = new FinishAnalyzer(new FinishInsightSettings()).Analyze(
            Ended(redKeeps: 2, blueKeeps: 0, positions: atBlueCore)
        );

        Assert.True(race.CoreRace);
        Assert.Equal(2, race.AttackersAtOtherCore);
        Assert.Equal(1, race.KeepsLostEach);
        Assert.False(oneSided.CoreRace);
    }

    [Fact]
    public void Comeback_IsTheSurvivingTeamBehindOnBuildingsOrLevels()
    {
        ComebackInsights buildings = new ComebackAnalyzer(new ComebackInsightSettings()).Analyze(
            Ended(redKeeps: 1, blueKeeps: 2, blueForts: 2, blueTowers: 1)
        );
        Assert.True(buildings.Comeback);
        Assert.Equal(4, buildings.BuildingDeficit);

        ReplaySnapshot levels = Ended(redKeeps: 1, blueKeeps: 1) with
        {
            TeamLevels = new Dictionary<int, IReadOnlyDictionary<int, TimeSpan>>
            {
                [0] = Levels(minutesPerLevel: 1.5),
                [1] = Levels(minutesPerLevel: 1.0),
            },
        };
        ComebackInsights levelComeback = new ComebackAnalyzer(
            new ComebackInsightSettings()
        ).Analyze(levels);
        Assert.True(levelComeback.MaxLevelDeficit >= 3);
        Assert.True(levelComeback.Comeback);

        Assert.False(
            new ComebackAnalyzer(new ComebackInsightSettings())
                .Analyze(Ended(redKeeps: 2, blueKeeps: 1))
                .Comeback
        );
    }

    /// <summary>Red's core fell at 20:00, so Blue's core is the one standing.</summary>
    private static ReplaySnapshot Ended(
        int redKeeps,
        int blueKeeps,
        int blueForts = 0,
        int blueTowers = 0,
        IHeroPositions positions = null
    )
    {
        var losses = new List<TownLoss>();
        losses.AddRange(
            Enumerable
                .Range(0, redKeeps)
                .Select(keep => new TownLoss(20 + keep, 1, true, TimeSpan.FromMinutes(17 + keep)))
        );
        losses.AddRange(
            Enumerable
                .Range(0, blueKeeps)
                .Select(keep => new TownLoss(keep, 0, true, TimeSpan.FromMinutes(15)))
        );
        losses.AddRange(
            Enumerable
                .Range(0, blueForts)
                .Select(fort => new TownLoss(10 + fort, 0, false, TimeSpan.FromMinutes(9)))
        );
        SnapshotPlayer[] players = Enumerable
            .Range(0, 10)
            .Select(index => InsightFixtures.Player(index, index / 5, "Hero" + index))
            .ToArray();
        return InsightFixtures.Snapshot(players, positions) with
        {
            End = End,
            CoreLostBy = 1,
            TownLosses = losses,
            TowerLosses = Enumerable
                .Range(0, blueTowers)
                .Select(_ => new TowerLoss(0, TimeSpan.FromMinutes(5)))
                .ToArray(),
        };
    }

    private static Dictionary<int, TimeSpan> Levels(double minutesPerLevel) =>
        Enumerable
            .Range(2, 19)
            .ToDictionary(level => level, level => TimeSpan.FromMinutes(level * minutesPerLevel));
}
