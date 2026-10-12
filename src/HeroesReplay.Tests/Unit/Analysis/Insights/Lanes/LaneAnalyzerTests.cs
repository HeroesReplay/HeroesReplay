using System.Collections.Generic;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Lanes;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis.Insights.Lanes;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class LaneAnalyzerTests
{
    private static readonly MapPoint Top = new(125, 160);
    private static readonly MapPoint Bottom = new(125, 60);

    [Fact]
    public void TwoSoloLanersInTheSameLane_AreHighConfidence()
    {
        LaneInsights lanes = Analyze(
            blueSoakers: new[] { ("Dehaka", Top) },
            redSoakers: new[] { ("Thrall", Top) }
        );

        Assert.Equal(3, lanes.LaneCount);
        Assert.All(lanes.Teams, team => Assert.Equal(LaneConfidence.High, team.Confidence));
        Assert.Equal("Dehaka", lanes.Teams[0].SoloLaner.Hero);
        Assert.Equal(1, lanes.Teams[0].SoloLaner.Lane);
        Assert.Equal(2, lanes.Teams[0].GroupLane);
        Assert.True(lanes.Teams[1].SoloLaner.Mirrored);
        Assert.Equal(1, lanes.Teams[1].SoloLaner.AloneShare);
    }

    [Fact]
    public void ALoneSoakerWithoutAMirror_IsMediumWithTopMinionDamage_ElseLow()
    {
        LaneInsights lanes = Analyze(
            blueSoakers: new[] { ("Dehaka", Top) },
            redSoakers: new (string, MapPoint)[0],
            soakerMinionDamage: 90000
        );
        Assert.Equal(LaneConfidence.Medium, lanes.Teams[0].Confidence);
        Assert.Equal(LaneConfidence.None, lanes.Teams[1].Confidence);
        Assert.Null(lanes.Teams[1].SoloLaner);

        LaneInsights low = Analyze(
            blueSoakers: new[] { ("Dehaka", Top) },
            redSoakers: new (string, MapPoint)[0],
            soakerMinionDamage: 0
        );
        Assert.Equal(LaneConfidence.Low, low.Teams[0].Confidence);
    }

    [Fact]
    public void TwoSoakers_AreLowAndTheMirroredOneIsTheSoloLaner()
    {
        LaneInsights lanes = Analyze(
            blueSoakers: new[] { ("Nazeebo", Bottom), ("Leoric", Top) },
            redSoakers: new[] { ("Thrall", Top) }
        );

        Assert.Equal(LaneConfidence.Low, lanes.Teams[0].Confidence);
        Assert.Equal(2, lanes.Teams[0].Soakers.Count);
        Assert.Equal("Leoric", lanes.Teams[0].SoloLaner.Hero);
    }

    [Fact]
    public void Abathur_IsNeverTheSoloLaner()
    {
        LaneInsights lanes = Analyze(
            blueSoakers: new[] { ("Abathur", Top) },
            redSoakers: new[] { ("Thrall", Top) }
        );

        Assert.Equal(LaneConfidence.None, lanes.Teams[0].Confidence);
        Assert.Equal(LaneConfidence.Medium, lanes.Teams[1].Confidence);
        Assert.False(lanes.Teams[1].SoloLaner.Mirrored);
    }

    [Fact]
    public void Aram_AndAMapWithoutTowns_HaveNoLanes()
    {
        ReplaySnapshot snapshot = InsightFixtures.Snapshot(
            new List<SnapshotPlayer>(),
            new ScriptedPositions()
        );

        Assert.Empty(
            new LaneAnalyzer(new LaneInsightSettings()).Analyze(snapshot with { Aram = true }).Teams
        );
        Assert.Empty(
            new LaneAnalyzer(new LaneInsightSettings())
                .Analyze(snapshot with { Towns = new Town[0] })
                .Teams
        );
    }

    /// <summary>Each team groups the rest of its five in the middle lane, a few units apart.</summary>
    private static LaneInsights Analyze(
        (string Hero, MapPoint At)[] blueSoakers,
        (string Hero, MapPoint At)[] redSoakers,
        int soakerMinionDamage = 90000
    )
    {
        var players = new List<SnapshotPlayer>();
        var positions = new ScriptedPositions();
        foreach (
            (int team, (string Hero, MapPoint At)[] soakers) in new[]
            {
                (0, blueSoakers),
                (1, redSoakers),
            }
        )
        {
            for (int slot = 0; slot < 5; slot++)
            {
                int index = team * 5 + slot;
                bool soaking = slot < soakers.Length;
                string hero = soaking ? soakers[slot].Hero : "Grouped" + index;
                players.Add(
                    InsightFixtures.Player(
                        index,
                        team,
                        hero,
                        new PlayerScore
                        {
                            MinionDamage = soaking ? soakerMinionDamage : 1000 * slot,
                        }
                    )
                );
                positions.At(
                    index,
                    soaking ? soakers[slot].At : new MapPoint(120 + slot * 3, 110 + slot)
                );
            }
        }

        return new LaneAnalyzer(new LaneInsightSettings()).Analyze(
            InsightFixtures.Snapshot(players, positions)
        );
    }
}
