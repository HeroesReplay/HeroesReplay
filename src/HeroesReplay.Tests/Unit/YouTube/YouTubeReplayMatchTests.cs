using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Search;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubeReplayMatchTests
{
    [Fact]
    public void IdsIn_ReadsTheTitleAndTheDescriptionLines()
    {
        Assert.Equal(
            new[] { 65550001, 65550002, 65550003 },
            YouTubeReplayMatch.IdsIn(
                "Cursed Hollow - Storm League - Diamond - 65550001",
                "Replay ID: 65550002\nHeroes Profile Match: https://www.heroesprofile.com/Match/Single/?replayID=65550003\nBuild 2.57.0.98285"
            )
        );
        Assert.Empty(YouTubeReplayMatch.IdsIn("Diamond 3 highlights", "Build 2.57.0.98285"));
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
