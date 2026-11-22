using System;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Pirates;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis.Insights.Pirates;

/// <summary>
/// Juice Pirates: a carrier (Lt. Morales with Medivac, or Medivh) and repeated dives into an
/// enemy base that has nobody home.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class PirateAnalyzerTests
{
    private static readonly MapPoint RedKeep = new(160, 110);
    private static readonly MapPoint Mid = new(125, 110);

    [Fact]
    public void MedivacDives_IntoAnEmptyBase_AreJuicePirates()
    {
        PirateInsights pirates = Analyze(Crew(medivac: true), Dives(), defendersHome: false);

        Assert.True(pirates.Pirates);
        Assert.Equal(0, pirates.Team);
        Assert.Equal("Lt. Morales", pirates.Carrier);
        Assert.True(pirates.Sanctification);
        Assert.True(pirates.Dives >= 2);
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
    public void OneDive_IsNotEnough()
    {
        Func<TimeSpan, MapPoint?> once = time =>
            time >= TimeSpan.FromMinutes(5) && time < TimeSpan.FromMinutes(5.5) ? RedKeep : Mid;
        Assert.False(Analyze(Crew(medivac: true), once, defendersHome: false).Pirates);
    }

    /// <summary>Three dives on Red's middle keep, a minute each, every four minutes.</summary>
    private static Func<TimeSpan, MapPoint?> Dives() =>
        time => time.TotalMinutes % 4 is >= 3 and < 4 ? RedKeep : Mid;

    private static SnapshotPlayer[] Crew(bool medivac)
    {
        SnapshotPlayer morales = InsightFixtures.Player(0, 0, "Lt. Morales") with
        {
            Talents = medivac ? new[] { "MedicHeroicAbilityMedivacDropship" } : new[] { "MedicHeroicAbilityStimDrone" },
        };
        SnapshotPlayer tyrael = InsightFixtures.Player(1, 0, "Tyrael") with
        {
            Talents = new[] { "TyraelHeroicAbilitySanctification" },
        };
        return new[]
        {
            morales,
            tyrael,
            InsightFixtures.Player(2, 0, "Azmodan"),
            InsightFixtures.Player(3, 0, "Zagara"),
            InsightFixtures.Player(4, 0, "Sylvanas"),
            InsightFixtures.Player(5, 1, "Muradin"),
            InsightFixtures.Player(6, 1, "Jaina"),
            InsightFixtures.Player(7, 1, "Uther"),
            InsightFixtures.Player(8, 1, "Valla"),
            InsightFixtures.Player(9, 1, "Diablo"),
        };
    }

    private static PirateInsights Analyze(
        SnapshotPlayer[] players,
        Func<TimeSpan, MapPoint?> divePath,
        bool defendersHome
    )
    {
        var positions = new ScriptedPositions();
        foreach (SnapshotPlayer player in players)
        {
            positions.Path(
                player.Index,
                player.Team == 0 ? divePath : _ => defendersHome ? RedKeep : InsightFixtures.BlueCore
            );
        }

        return new PirateAnalyzer(new PirateInsightSettings()).Analyze(
            InsightFixtures.Snapshot(players, positions)
        );
    }
}
