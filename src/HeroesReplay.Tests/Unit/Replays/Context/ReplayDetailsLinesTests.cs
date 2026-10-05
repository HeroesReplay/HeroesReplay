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
        Patch = true,
    };

    [Fact]
    public void Build_PutsThePatchAfterTheBans()
    {
        IReadOnlyList<string> lines = ReplayDetailsLines.Build(
            Everything,
            Loaded("2.57.0.98348", login: "viewer"),
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
                "Patch: 2.57.0.98348",
            },
            lines
        );
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

    private static LoadedReplay Loaded(string replayVersion, string login = null) =>
        new()
        {
            Replay = new Replay { ReplayVersion = replayVersion },
            HeroesProfileReplay = new HeroesProfileReplay { GameType = "Storm League" },
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
