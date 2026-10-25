using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.Shared;
using HeroesReplay.Tests.Unit.Support;
using Xunit;
using ReplayUnit = Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.Analysis.Insights;

/// <summary>The bundled replay through every analyzer, and the replay readers the analyzers rely on.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class MatchInsightsReaderTests : IClassFixture<ReplayFixture>
{
    private readonly ReplayFixture fixture;

    public MatchInsightsReaderTests(ReplayFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void Read_FillsEverySection_AndRoundTripsThroughJson()
    {
        MatchInsights insights = MatchInsightsReader
            .Create(new InsightsSettings(), GuideCatalog.Embedded())
            .Read(fixture.Replay, Array.Empty<Hero>());

        Assert.Equal(fixture.Replay.Players.Length, insights.Players.Lines.Count);
        Assert.Equal(2, insights.Structures.Teams.Count);
        Assert.NotNull(insights.Players.Mvp);
        Assert.Null(insights.Focus.Top);

        string json = InsightsJson.Write(insights);
        MatchInsights read = InsightsJson.Parse(json);

        Assert.Contains("\"confidence\": \"", json, StringComparison.Ordinal);
        Assert.Equal(MatchInsights.CurrentVersion, read.Version);
        Assert.Equal(insights.Players.Lines.Count, read.Players.Lines.Count);
        Assert.Equal(insights.Lanes.LaneCount, read.Lanes.LaneCount);
        Assert.Equal(insights.Comeback, read.Comeback);
        Assert.Equal(insights.Finish, read.Finish);
    }

    [Fact]
    public void Snapshot_HoldsTheMapAwardsMapValuesAndKillingBlows()
    {
        // #512: what the burst, objective and award analyzers read.
        ReplaySnapshot snapshot = ReplaySnapshotReader.Read(
            fixture.Replay,
            Array.Empty<Hero>(),
            new InsightsSettings()
        );

        Assert.Equal("Garden of Terror", snapshot.Map);
        Assert.Contains(snapshot.Players, player => player.Score.HasAward("MVP"));
        Assert.All(snapshot.Players, player => Assert.NotEmpty(player.Score.MapValues));
        Assert.NotEmpty(snapshot.KillingBlows);
        Assert.Equal(
            snapshot.KillingBlows.Select(blow => blow.Second).OrderBy(second => second),
            snapshot.KillingBlows.Select(blow => blow.Second)
        );
        Assert.All(
            snapshot.KillingBlows,
            blow => Assert.InRange(blow.KillerIndex, 0, snapshot.Players.Count - 1)
        );

        MatchInsights insights = MatchInsightsReader
            .Create(new InsightsSettings(), GuideCatalog.Embedded())
            .Read(fixture.Replay, Array.Empty<Hero>());
        MatchInsights read = InsightsJson.Parse(InsightsJson.Write(insights));
        Assert.Null(read.Objectives.Label);
        Assert.Equal(insights.Awards.Awards.Count, read.Awards.Awards.Count);
        Assert.Equal(insights.Bursts.Players.Count, read.Bursts.Players.Count);
    }

    [Fact]
    public void StatEvent_ReadsNamedTextAndIntegerFields_InEverySection()
    {
        var capture = new TrackerEvent
        {
            TrackerEventType = ReplayTrackerEvents.TrackerEventType.StatGameEvent,
            TimeSpan = TimeSpan.FromSeconds(101),
            Data = new TrackerEventStructure
            {
                dictionary = new Dictionary<int, TrackerEventStructure>
                {
                    [0] = new() { blob = Encoding.UTF8.GetBytes("TownStructureDeath") },
                    [1] = new() { array = new[] { Field("UnitType", text: "TownTownHallL3") } },
                    [2] = new()
                    {
                        array = new[]
                        {
                            Field("TownID", 8),
                            Field("KillingPlayer", 1),
                            Field("KillingPlayer", 5),
                        },
                    },
                    [3] = new()
                    {
                        optionalData = new TrackerEventStructure
                        {
                            array = new[] { Field("GameTime", 589) },
                        },
                    },
                },
            },
        };

        StatEvent stat = StatEvent.From(capture);

        Assert.Equal("TownStructureDeath", stat.Name);
        Assert.Equal(TimeSpan.FromSeconds(101), stat.Time);
        Assert.Equal("TownTownHallL3", stat.Text("UnitType"));
        Assert.Equal(8, stat.Int("TownID"));
        Assert.Equal(new[] { 1, 5 }, stat.Ints("KillingPlayer"));
        Assert.Equal(589, stat.Int("GameTime"));
        Assert.Null(stat.Int("Missing"));
        Assert.Null(
            StatEvent.From(
                new TrackerEvent
                {
                    TrackerEventType = ReplayTrackerEvents.TrackerEventType.UnitBornEvent,
                }
            )
        );
    }

    [Fact]
    public void HeroPositions_UseTheLastFreshSampleWhileTheHeroIsAlive()
    {
        var unit = new ReplayUnit
        {
            TimeSpanBorn = TimeSpan.Zero,
            TimeSpanDied = TimeSpan.FromSeconds(60),
            Positions = new List<Position>
            {
                new()
                {
                    TimeSpan = TimeSpan.FromSeconds(10),
                    Point = new Point { X = 1, Y = 2 },
                },
                new()
                {
                    TimeSpan = TimeSpan.FromSeconds(20),
                    Point = new Point { X = 5, Y = 6 },
                },
            },
        };
        var positions = new HeroPositions(
            new[] { new Player { HeroUnits = new List<ReplayUnit> { unit } } },
            TimeSpan.FromSeconds(4)
        );

        Assert.True(positions.TryGet(0, TimeSpan.FromSeconds(12), out MapPoint point));
        Assert.Equal(new MapPoint(1, 2), point);
        Assert.True(positions.TryGet(0, TimeSpan.FromSeconds(20), out point));
        Assert.Equal(new MapPoint(5, 6), point);
        Assert.False(positions.TryGet(0, TimeSpan.FromSeconds(15), out _));
        Assert.False(positions.TryGet(0, TimeSpan.FromSeconds(5), out _));
        Assert.False(positions.TryGet(0, TimeSpan.FromSeconds(61), out _));
        Assert.False(positions.TryGet(3, TimeSpan.FromSeconds(12), out _));
    }

    private static TrackerEventStructure Field(
        string key,
        long? number = null,
        string text = null
    ) =>
        new()
        {
            dictionary = new Dictionary<int, TrackerEventStructure>
            {
                [0] = new()
                {
                    dictionary = new Dictionary<int, TrackerEventStructure>
                    {
                        [0] = new() { blob = Encoding.UTF8.GetBytes(key) },
                    },
                },
                [1] = new()
                {
                    blob = text == null ? null : Encoding.UTF8.GetBytes(text),
                    vInt = number,
                },
            },
        };
}
