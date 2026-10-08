using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Replays.Context;
using HeroesReplay.Core.Requests;
using Xunit;

namespace HeroesReplay.Tests.Unit.Replays.Context;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayDetailsLinesTests
{
    private static readonly ReplayDetailsWriterSettings Everything = new()
    {
        Enabled = true,
        Requestor = true,
        GameType = true,
        Bans = true,
        Region = true,
        Patch = true,
    };

    [Fact]
    public void Build_PutsTheRegionAndThePatchAfterTheBans()
    {
        IReadOnlyList<string> lines = ReplayDetailsLines.Build(
            Everything,
            Loaded("2.57.0.98348", login: "viewer", playerRegion: 2),
            Bans(new[] { "Abathur", "Medivh" }, new[] { "Tyrande" })
        );

        Assert.Equal(
            new[]
            {
                "Requestor: viewer",
                "Storm League",
                "Bans:",
                "T1: Abathur",
                "T1: Medivh",
                "T2: Tyrande",
                "Region: EU",
                "Patch: 2.57.0.98348",
            },
            lines
        );
    }

    [Fact]
    public void Build_LeavesTheRegionOutWhenItIsUnknown()
    {
        IReadOnlyList<string> lines = ReplayDetailsLines.Build(
            Everything,
            Loaded("2.57.0.98348", playerRegion: 98),
            null
        );

        Assert.Equal(new[] { "Storm League", "Patch: 2.57.0.98348" }, lines);
    }

    [Fact]
    public void Build_LeavesTheRegionOutWhenSwitchedOff()
    {
        var writer = new ReplayDetailsWriterSettings
        {
            Enabled = true,
            Region = false,
            Patch = true,
        };

        IReadOnlyList<string> lines = ReplayDetailsLines.Build(
            writer,
            Loaded("2.57.0.98348", playerRegion: 1),
            null
        );

        Assert.Equal(new[] { "Patch: 2.57.0.98348" }, lines);
    }

    [Fact]
    public void Region_ReadsTheReplayPlayersBeforeHeroesProfileAndSkipsAnAi()
    {
        LoadedReplay loaded = Loaded("2.57.0.98348", playerRegion: 3, profileRegion: 1);

        Assert.Equal("KR", ReplayDetailsLines.Region(loaded));
    }

    [Fact]
    public void Region_UsesTheHeroesProfileRegionWhenTheReplayHasNone()
    {
        Assert.Equal("CN", ReplayDetailsLines.Region(Loaded("2.57.0.98348", profileRegion: 5)));
    }

    [Fact]
    public void Region_IsNullWhenNoRegionIsKnown()
    {
        Assert.Null(ReplayDetailsLines.Region(Loaded("2.57.0.98348")));
        Assert.Null(ReplayDetailsLines.Region(Loaded("2.57.0.98348", playerRegion: 0)));
        Assert.Null(ReplayDetailsLines.Region(null));
        Assert.Null(ReplayDetailsLines.Region(new LoadedReplay()));
    }

    [Theory]
    [InlineData(1, "NA")]
    [InlineData(2, "EU")]
    [InlineData(3, "KR")]
    [InlineData(5, "CN")]
    [InlineData(0, null)]
    [InlineData(4, null)]
    [InlineData(98, null)]
    [InlineData(null, null)]
    public void RegionName_NamesTheBattleNetRegions(int? id, string expected)
    {
        Assert.Equal(expected, ReplayDetailsLines.RegionName(id));
    }

    [Fact]
    public void Build_WritesThePatchWhenThereAreNoBans()
    {
        IReadOnlyList<string> lines = ReplayDetailsLines.Build(
            Everything,
            Loaded("2.57.0.98348"),
            Bans(new string[0], new string[0])
        );

        Assert.Equal(new[] { "Storm League", "Patch: 2.57.0.98348" }, lines);
    }

    [Fact]
    public void Build_LeavesThePatchOutWhenSwitchedOff()
    {
        var writer = new ReplayDetailsWriterSettings
        {
            Enabled = true,
            GameType = true,
            Patch = false,
        };

        IReadOnlyList<string> lines = ReplayDetailsLines.Build(
            writer,
            Loaded("2.57.0.98348"),
            null
        );

        Assert.Equal(new[] { "Storm League" }, lines);
    }

    [Fact]
    public void Build_KeepsTheOtherTeamWhenOneTeamHasNoBanEntry()
    {
        var bans = new Dictionary<int, IReadOnlyCollection<string>> { [1] = new[] { "Tyrande" } };

        IReadOnlyList<string> lines = ReplayDetailsLines.Build(
            new ReplayDetailsWriterSettings { Enabled = true, Bans = true },
            Loaded("2.57.0.98348"),
            bans
        );

        Assert.Equal(new[] { "Bans:", "T2: Tyrande" }, lines);
    }

    [Fact]
    public void Patch_UsesTheHeroesProfileVersionWhenTheReplayHasNone()
    {
        LoadedReplay loaded = Loaded(replayVersion: null);
        loaded.HeroesProfileReplay.GameVersion = "2.57.0.98304";

        Assert.Equal("2.57.0.98304", ReplayDetailsLines.Patch(loaded));
    }

    [Fact]
    public void Patch_IsNullWhenNoVersionIsKnown()
    {
        Assert.Null(ReplayDetailsLines.Patch(Loaded(replayVersion: " ")));
        Assert.Null(ReplayDetailsLines.Patch(null));
    }

    private static LoadedReplay Loaded(
        string replayVersion,
        string login = null,
        int? playerRegion = null,
        int? profileRegion = null
    ) =>
        new()
        {
            Replay = new Replay
            {
                ReplayVersion = replayVersion,
                Players =
                    playerRegion == null
                        ? null
                        : new[]
                        {
                            new Player { BattleNetRegionId = 0, PlayerType = PlayerType.Computer },
                            new Player
                            {
                                BattleNetRegionId = playerRegion.Value,
                                PlayerType = PlayerType.Human,
                            },
                        },
            },
            HeroesProfileReplay = new HeroesProfileReplay
            {
                GameType = "Storm League",
                Region = profileRegion,
            },
            RewardQueueItem =
                login == null
                    ? null
                    : new RewardQueueItem { Request = new RewardRequest { Login = login } },
        };

    private static Dictionary<int, IReadOnlyCollection<string>> Bans(
        string[] team1,
        string[] team2
    ) => new() { [0] = team1, [1] = team2 };
}
