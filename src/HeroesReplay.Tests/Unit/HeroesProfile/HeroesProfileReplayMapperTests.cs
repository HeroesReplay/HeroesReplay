using System.Linq;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.HeroesProfile.Client;
using HeroesReplay.HeroesProfile.Client.Replays;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroesProfileReplayMapperTests
{
    [Fact]
    public void ToReplay_MapsListRow()
    {
        var row = new ReplaysGetResponse_replays
        {
            ReplayID = 65267632,
            Region = 1,
            Fingerprint = "abc",
            GameType = "Storm League",
            GameVersion = "2.55.17.98025",
            GameMap = "Haunted Mines",
            GameDate = "2026-09-19 00:00:00",
            Parsed = 1,
            Deleted = 0,
            Downloadable = true,
        };

        var replay = HeroesProfileReplayMapper.ToReplay(row);

        Assert.Equal(65267632, replay.Id);
        Assert.Equal(1, replay.Region);
        Assert.Equal("Storm League", replay.GameType);
        Assert.Equal("Haunted Mines", replay.Map);
        Assert.Equal("2.55.17.98025", replay.GameVersion);
        Assert.True(replay.Downloadable);
        Assert.Equal(0, replay.Deleted);
        Assert.Equal("abc", replay.Fingerprint);
    }

    [Fact]
    public void ToReplays_SkipsMissingIds()
    {
        var page = new ReplaysGetResponse
        {
            MaxReplayId = 10,
            NextAfter = 2,
            Replays =
            [
                new ReplaysGetResponse_replays { ReplayID = 2, GameType = "sl" },
                new ReplaysGetResponse_replays { ReplayID = null },
            ],
        };

        var replays = HeroesProfileReplayMapper.ToReplays(page).ToArray();

        Assert.Single(replays);
        Assert.Equal(2, replays[0].Id);
        Assert.Equal("sl", replays[0].GameType);
    }

    [Fact]
    public void Factory_CreatesClientWithV1Base()
    {
        var client = HeroesProfileClientFactory.Create("test-key");

        Assert.NotNull(client.Replays);
        Assert.NotNull(client.Download.Replay);
        Assert.NotNull(client.Replay);
    }
}
