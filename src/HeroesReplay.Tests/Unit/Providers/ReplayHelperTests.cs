using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Providers;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayHelperTests
{
    [Theory]
    [InlineData("65267632.StormReplay", 65267632)]
    [InlineData("65267632_Storm League_Gold_Haunted Mines_abc.StormReplay", 65267632)]
    public void TryGetReplayId_ParsesIdPrefix(string name, int expected)
    {
        var helper = new ReplayHelper(
            NullLogger<ReplayHelper>.Instance,
            new AppSettings
            {
                StormReplay = new StormReplaySettings
                {
                    Seperator = "_",
                    FileExtension = ".StormReplay",
                },
            }
        );

        Assert.True(helper.TryGetReplayId(name, out int replayId));
        Assert.Equal(expected, replayId);
    }
}
