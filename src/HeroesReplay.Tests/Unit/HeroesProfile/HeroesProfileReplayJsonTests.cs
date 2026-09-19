using System.Text.Json;
using HeroesReplay.Core.Models;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroesProfileReplayJsonTests
{
    [Fact]
    public void MinIdPayload_DeserializesNumericRegion()
    {
        const string json = """
            [{"replayID":65267450,"region":2,"url":"https://storage.cloud.google.com/heroesprofile-replay-storage/x","fingerprint":"abc","parsed":1,"valid":1,"deleted":null,"game_type":"Storm League","game_version":"2.55.17.98025","game_map":"Haunted Mines","rank":"Gold"}]
            """;

        var replays = JsonSerializer.Deserialize<HeroesProfileReplay[]>(json);

        Assert.NotNull(replays);
        Assert.Single(replays);
        Assert.Equal(65267450, replays[0].Id);
        Assert.Equal(2, replays[0].Region);
        Assert.Equal("Storm League", replays[0].GameType);
        Assert.Equal("Haunted Mines", replays[0].Map);
        Assert.Equal("Gold", replays[0].Rank);
    }

    [Fact]
    public void MinIdPayload_DeserializesDocExampleStringRegion()
    {
        const string json = """
            [{"replayID":1,"region":"US","url":"http://heroesprofile.s3.amazonaws.com/x.StormReplay","fingerprint":"x","parsed":1,"valid":1,"deleted":null,"game_type":"ARAM","game_version":"2.53.0.83086","game_map":"Lost Cavern","rank":"Diamond"}]
            """;

        var replays = JsonSerializer.Deserialize<HeroesProfileReplay[]>(json);

        Assert.Equal(1, replays[0].Region);
    }

    [Fact]
    public void V1ReplayPage_Deserializes()
    {
        const string json = """
            {"replays":[{"replayID":65267632,"region":1,"fingerprint":"abc","game_type":"Storm League","game_version":"2.55.17.98025","game_map":"Haunted Mines","game_date":"2026-09-19 00:00:00","parsed":1,"deleted":0,"downloadable":true}],"next_after":65267632,"max_replay_id":65287216}
            """;

        var page = JsonSerializer.Deserialize<HeroesProfileReplayPage>(json);

        Assert.NotNull(page);
        Assert.Equal(65287216, page.MaxReplayId);
        Assert.Equal(65267632, page.NextAfter);
        Assert.True(page.Replays[0].Downloadable);
        Assert.Equal(0, page.Replays[0].Deleted);
        Assert.Equal("Haunted Mines", page.Replays[0].Map);
    }
}
