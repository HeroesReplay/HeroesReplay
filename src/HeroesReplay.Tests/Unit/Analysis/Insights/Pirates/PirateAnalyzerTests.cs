using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Pirates;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis.Insights.Pirates;

/// <summary>
/// Juice Pirates: a carrier (Lt. Morales with Medivac, or Medivh) in repeated raids on an enemy
/// base that has nobody home, going structure to structure, on a team that does most of the
/// game's building damage.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class PirateAnalyzerTests
{
    private static readonly MapPoint RedKeep = new(160, 110);
    private static readonly MapPoint Mid = new(125, 110);

    [Fact]
    public void MedivacRaids_IntoAnEmptyBase_AreJuicePirates()
    {
        PirateInsights pirates = Analyze(Crew(medivac: true), Dives(), defendersHome: false);

        Assert.True(pirates.Pirates);
        Assert.Equal(0, pirates.Team);
        Assert.Equal("Lt. Morales", pirates.Carrier);
        Assert.True(pirates.Sanctification);
        Assert.True(pirates.Raids >= 2);
        Assert.All(pirates.DiveLog, dive => Assert.True(dive.Carrier));
        Assert.Contains("Tyrael", pirates.Heroes);
    }

    [Fact]
    public void MoralesWithoutMedivac_IsNotACarrier()
    {
        Assert.False(Analyze(Crew(medivac: false), Dives(), defendersHome: false).Pirates);
    }

    [Fact]
    public void ADefendedBase_IsAPush_NotAPirateDive()
    {
        Assert.False(Analyze(Crew(medivac: true), Dives(), defendersHome: true).Pirates);
    }

    [Fact]
    public void AnEmptyBase_BecauseTheDefendersAreDead_IsAPush()
    {
        var positions = new ScriptedPositions();
        foreach (SnapshotPlayer player in Crew(medivac: true))
        {
            positions.Path(player.Index, player.Team == 0 ? Dives() : _ => null);
        }

        PirateInsights pirates = new PirateAnalyzer(new PirateInsightSettings()).Analyze(
            Snapshot(Crew(medivac: true), positions, Buildings())
        );

        Assert.False(pirates.Pirates);
        Assert.Equal(0, pirates.Dives);
    }

    [Fact]
    public void OneRaid_IsNotEnough()
    {
        Func<TimeSpan, MapPoint?> once = time =>
            time >= TimeSpan.FromMinutes(3) && time < TimeSpan.FromMinutes(4) ? RedKeep : Mid;
        Assert.False(Analyze(Crew(medivac: true), once, defendersHome: false).Pirates);
    }

    /// <summary>
    /// Replay 66787940: Medivh in the game and three heroes at an enemy fort while the enemy was
    /// elsewhere, but no buildings fell to it. A macro push is not a backdoor.
    /// </summary>
    [Fact]
    public void DivesThatDestroyNoBuildings_AreNotRaids()
    {
        PirateInsights pirates = Analyze(
            Crew(medivac: true),
            Dives(),
            defendersHome: false,
            buildings: Array.Empty<StructureLoss>()
        );

        Assert.False(pirates.Pirates);
        Assert.True(pirates.Dives >= 2);
        Assert.Equal(0, pirates.Raids);
    }

    [Fact]
    public void AMedivhWhoNeverDives_CarriesNobody()
    {
        SnapshotPlayer[] crew = Crew(medivac: false)
            .Select(player =>
                player.Index == 0 ? InsightFixtures.Player(0, 0, "Medivh") with { Score = Siege } : player
            )
            .ToArray();
        var positions = new ScriptedPositions();
        foreach (SnapshotPlayer player in crew)
        {
            positions.Path(
                player.Index,
                player.Team == 1 ? _ => InsightFixtures.BlueCore
                    : player.Index == 0 ? _ => Mid
                    : Dives()
            );
        }

        PirateInsights pirates = new PirateAnalyzer(new PirateInsightSettings()).Analyze(
            Snapshot(crew, positions, Buildings())
        );

        Assert.Equal("Medivh", pirates.Carrier);
        Assert.False(pirates.Pirates);
        Assert.Equal(0, pirates.Raids);
        Assert.All(pirates.DiveLog, dive => Assert.False(dive.Carrier));
    }

    [Fact]
    public void RaidsByATeamWithLittleBuildingDamage_AreNotJuicePirates()
    {
        SnapshotPlayer[] crew = Crew(medivac: true)
            .Select(player =>
                player with
                {
                    Score = new PlayerScore { StructureDamage = player.Team == 0 ? 10_000 : 40_000 },
                }
            )
            .ToArray();

        PirateInsights pirates = Analyze(crew, Dives(), defendersHome: false);

        Assert.True(pirates.Raids >= 2);
        Assert.Equal(0.2, pirates.StructureDamageShare, 3);
        Assert.False(pirates.Pirates);
    }

    [Fact]
    public void BuildingsTheDefendersFinishOff_DoNotCount()
    {
        IReadOnlyList<StructureLoss> denied = Buildings()
            .Select(loss => loss with { KillerIndex = 5 })
            .ToArray();

        PirateInsights pirates = Analyze(
            Crew(medivac: true),
            Dives(),
            defendersHome: false,
            buildings: denied
        );

        Assert.Equal(0, pirates.Raids);
    }

    private static readonly PlayerScore Siege = new() { StructureDamage = 30_000 };

    /// <summary>Five dives on Red's middle keep, a minute each, every four minutes.</summary>
    private static Func<TimeSpan, MapPoint?> Dives() =>
        time => time.TotalMinutes % 4 is >= 3 and < 4 ? RedKeep : Mid;

    /// <summary>Two buildings fall around Red's middle keep in each dive, last hit by the pirates.</summary>
    private static IReadOnlyList<StructureLoss> Buildings() =>
        Enumerable
            .Range(0, 5)
            .SelectMany(dive =>
                new[]
                {
                    new StructureLoss(
                        1,
                        TimeSpan.FromMinutes(dive * 4 + 3.3),
                        new MapPoint(165, 115),
                        2
                    ),
                    new StructureLoss(
                        1,
                        TimeSpan.FromMinutes(dive * 4 + 3.6),
                        new MapPoint(165, 105),
                        -1
                    ),
                }
            )
            .ToArray();

    private static SnapshotPlayer[] Crew(bool medivac)
    {
        SnapshotPlayer morales = InsightFixtures.Player(0, 0, "Lt. Morales", Siege) with
        {
            Talents = medivac
                ? new[] { "MedicHeroicAbilityMedivacDropship" }
                : new[] { "MedicHeroicAbilityStimDrone" },
        };
        SnapshotPlayer tyrael = InsightFixtures.Player(1, 0, "Tyrael", Siege) with
        {
            Talents = new[] { "TyraelHeroicAbilitySanctification" },
        };
        return new[]
        {
            morales,
            tyrael,
            InsightFixtures.Player(2, 0, "Azmodan", Siege),
            InsightFixtures.Player(3, 0, "Zagara", Siege),
            InsightFixtures.Player(4, 0, "Sylvanas", Siege),
            InsightFixtures.Player(5, 1, "Muradin"),
            InsightFixtures.Player(6, 1, "Jaina"),
            InsightFixtures.Player(7, 1, "Uther"),
            InsightFixtures.Player(8, 1, "Valla"),
            InsightFixtures.Player(9, 1, "Diablo"),
        };
    }

    private static ReplaySnapshot Snapshot(
        SnapshotPlayer[] players,
        IHeroPositions positions,
        IReadOnlyList<StructureLoss> buildings
    ) => InsightFixtures.Snapshot(players, positions) with { StructureLosses = buildings };

    private static PirateInsights Analyze(
        SnapshotPlayer[] players,
        Func<TimeSpan, MapPoint?> divePath,
        bool defendersHome,
        IReadOnlyList<StructureLoss> buildings = null
    )
    {
        var positions = new ScriptedPositions();
        foreach (SnapshotPlayer player in players)
        {
            positions.Path(
                player.Index,
                player.Team == 0
                    ? divePath
                    : _ => defendersHome ? RedKeep : InsightFixtures.BlueCore
            );
        }

        return new PirateAnalyzer(new PirateInsightSettings()).Analyze(
            Snapshot(players, positions, buildings ?? Buildings())
        );
    }
}
