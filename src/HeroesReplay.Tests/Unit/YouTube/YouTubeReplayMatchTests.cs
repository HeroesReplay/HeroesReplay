using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.YouTube;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubeReplayMatchTests
{
    [Theory]
    [InlineData("Volskaya Foundry - 65389750 - Storm League - Diamond", 65389750, true)]
    [InlineData(
        "Heroes Profile Match: https://www.heroesprofile.com/Match/Single/?replayID=65389750",
        65389750,
        true
    )]
    [InlineData("Volskaya Foundry - 653897501 - Storm League", 65389750, false)]
    [InlineData("no id here", 65389750, false)]
    public void Mentions_RequiresTheWholeReplayId(string text, int replayId, bool expected)
    {
        Assert.Equal(expected, YouTubeReplayMatch.Mentions(text, replayId));
    }

    [Fact]
    public void FromEntry_PrefersTheStoredIdThenTheTitle()
    {
        Assert.Equal(
            12,
            YouTubeReplayMatch.FromEntry(new YouTubeEntry { ReplayId = 12, Title = "Map - 99" })
        );
        Assert.Equal(
            65389750,
            YouTubeReplayMatch.FromEntry(
                new YouTubeEntry { Title = "Volskaya Foundry - 65389750 - Storm League - Diamond" }
            )
        );
        Assert.Null(YouTubeReplayMatch.FromEntry(new YouTubeEntry { Title = "no id" }));
    }
}
