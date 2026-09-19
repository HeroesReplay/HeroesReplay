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
}
