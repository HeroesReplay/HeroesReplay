using System;
using System.Linq;
using HeroesReplay.Core.HeroesProfile;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayDownloadPickTests
{
    private static readonly string[] Installed = { "2.55.17.98025", "2.57.0.98304" };

    [Fact]
    public void Launchable_DropsAMissingBuildAndKeepsAnInstalledOne()
    {
        ReplayListing page = Page(
            new HeroesProfileReplay { Id = 11, GameVersion = "2.57.0.98285" },
            new HeroesProfileReplay { Id = 12, GameVersion = "2.57.0.98304" }
        );

        ReplayListing kept = ReplayDownloadPick.Launchable(page, Installed);

        Assert.Equal(12, Assert.Single(kept.Playable).Id);
        Assert.Equal("2.57.0.98304", kept.Playable[0].GameVersion);
        Assert.True(kept.HadRows);
        Assert.Equal(12, kept.HighestId);
        Assert.Equal(12, kept.NextAfter);
    }

    [Fact]
    public void Launchable_LetsTheCursorPassAPageOfMissingBuilds()
    {
        ReplayListing page = new ReplayListing(
            new[]
            {
                new HeroesProfileReplay { Id = 65550852, GameVersion = "2.57.0.98285" },
                new HeroesProfileReplay { Id = 65550901, GameVersion = "2.57.0.98297" },
            },
            hadRows: true,
            highestId: 65550901,
            nextAfter: 65550910
        );

        ReplayListing kept = ReplayDownloadPick.Launchable(page, Installed);

        Assert.Empty(kept.Playable);
        Assert.True(kept.HadRows);
        Assert.Equal(65550910, ReplayListCursor.AfterRejectedPage(65550000, kept));
    }

    [Fact]
    public void Launchable_DoesNotLetTheCursorSkipAnInstalledReplay()
    {
        ReplayListing page = new ReplayListing(
            new[]
            {
                new HeroesProfileReplay { Id = 65550901, GameVersion = "2.57.0.98285" },
                new HeroesProfileReplay { Id = 65551020, GameVersion = "2.57.0.98304" },
            },
            hadRows: true,
            highestId: 65551020,
            nextAfter: 65551030
        );

        ReplayListing kept = ReplayDownloadPick.Launchable(page, Installed);

        Assert.Equal(65551020, Assert.Single(kept.Playable).Id);
        Assert.Null(ReplayListCursor.AfterRejectedPage(65550900, kept));
    }

    [Fact]
    public void Launchable_KeepsAnUnknownVersion()
    {
        ReplayListing page = Page(new HeroesProfileReplay { Id = 3, GameVersion = null });

        ReplayListing kept = ReplayDownloadPick.Launchable(page, Installed);

        Assert.Equal(3, Assert.Single(kept.Playable).Id);
    }

    [Fact]
    public void Launchable_KeepsRowsWhenNoClientWasRead()
    {
        ReplayListing page = Page(
            new HeroesProfileReplay { Id = 11, GameVersion = "2.57.0.98285" }
        );

        ReplayListing kept = ReplayDownloadPick.Launchable(page, Array.Empty<string>());

        Assert.Equal(11, Assert.Single(kept.Playable).Id);
    }

    [Fact]
    public void Launchable_EmptyWhenThePageIsMissing()
    {
        ReplayListing kept = ReplayDownloadPick.Launchable(null, Installed);

        Assert.False(kept.HadRows);
        Assert.Empty(kept.Playable);
        Assert.Equal(0, kept.HighestId);
        Assert.Null(kept.NextAfter);
    }

    private static ReplayListing Page(params HeroesProfileReplay[] replays)
    {
        int highest = replays.Max(replay => replay.Id);
        return new ReplayListing(replays, hadRows: true, highestId: highest, nextAfter: highest);
    }
}
