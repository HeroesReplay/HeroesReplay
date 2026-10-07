using System;
using System.Linq;
using HeroesReplay.Core.HeroesProfile;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayDownloadPickTests
{
    private static readonly string[] Installed = { "2.55.17.98025", "2.57.0.98304" };
    private const string Floor = "2.57.0.98285";

    [Fact]
    public void Launchable_KeepsAMissingOlderBuildOnTheSupportedLine()
    {
        // Spectate opens it through HeroesSwitcher and Blizzard downloads that client.
        ReplayListing page = Page(
            new HeroesProfileReplay { Id = 11, GameVersion = "2.57.0.98285" },
            new HeroesProfileReplay { Id = 12, GameVersion = "2.57.0.98304" }
        );

        ReplayListing kept = ReplayDownloadPick.Launchable(page, Installed, null, Floor);

        Assert.Equal(new[] { 11, 12 }, kept.Playable.Select(replay => replay.Id));
        Assert.True(kept.HadRows);
        Assert.Equal(12, kept.HighestId);
        Assert.Equal(12, kept.NextAfter);
    }

    [Fact]
    public void Launchable_DropsAHeldBuildANewerBuildAndAMissingBuildBelowTheLine()
    {
        ReplayListing page = Page(
            new HeroesProfileReplay { Id = 11, GameVersion = "2.57.0.98285" },
            new HeroesProfileReplay { Id = 12, GameVersion = "2.57.0.98400" },
            new HeroesProfileReplay { Id = 13, GameVersion = "2.55.17.97000" },
            new HeroesProfileReplay { Id = 14, GameVersion = "2.55.17.98025" },
            new HeroesProfileReplay { Id = 15, GameVersion = "2.57.0.98290" }
        );

        ReplayListing kept = ReplayDownloadPick.Launchable(
            page,
            Installed,
            new[] { "2.57.0.98285" },
            Floor
        );

        // 11 is held after a failed download, 12 is newer than every installed client, and 13
        // is a missing build below the supported line. 14 is installed, 15 can be downloaded.
        Assert.Equal(new[] { 14, 15 }, kept.Playable.Select(replay => replay.Id));
    }

    [Fact]
    public void Launchable_LetsTheCursorPassAPageOfBuildsThatCannotLaunch()
    {
        ReplayListing page = new ReplayListing(
            new[]
            {
                new HeroesProfileReplay { Id = 65550852, GameVersion = "2.57.0.98285" },
                new HeroesProfileReplay { Id = 65550901, GameVersion = "2.57.0.98400" },
            },
            hadRows: true,
            highestId: 65550901,
            nextAfter: 65550910
        );

        ReplayListing kept = ReplayDownloadPick.Launchable(
            page,
            Installed,
            new[] { "2.57.0.98285" },
            Floor
        );

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
                new HeroesProfileReplay { Id = 65550901, GameVersion = "2.57.0.98400" },
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

    [Fact]
    public void FirstOnCurrentPatch_TakesTheNewestInstalledBuildOverALowerId()
    {
        string[] installed = { "2.57.0.98304", "2.57.0.98348" };
        HeroesProfileReplay[] candidates =
        {
            new() { Id = 21, GameVersion = "2.57.0.98304" },
            new() { Id = 23, GameVersion = "2.57.0.98348" },
            new() { Id = 22, GameVersion = "2.57.0.98348" },
        };

        HeroesProfileReplay first = ReplayDownloadPick.FirstOnCurrentPatch(candidates, installed);

        Assert.Equal(22, first.Id);
    }

    [Fact]
    public void FirstOnCurrentPatch_NullWhenOnlyAnOlderBuildIsListed()
    {
        HeroesProfileReplay[] candidates =
        {
            new() { Id = 21, GameVersion = "2.57.0.98304" },
        };

        Assert.Null(
            ReplayDownloadPick.FirstOnCurrentPatch(
                candidates,
                new[] { "2.57.0.98304", "2.57.0.98348" }
            )
        );
    }

    [Fact]
    public void FirstOnCurrentPatch_TakesTheLowestIdWhenNoClientWasRead()
    {
        HeroesProfileReplay[] candidates =
        {
            new() { Id = 22, GameVersion = "2.57.0.98348" },
            new() { Id = 21, GameVersion = "2.57.0.98304" },
        };

        Assert.Equal(
            21,
            ReplayDownloadPick.FirstOnCurrentPatch(candidates, Array.Empty<string>()).Id
        );
    }

    private static ReplayListing Page(params HeroesProfileReplay[] replays)
    {
        int highest = replays.Max(replay => replay.Id);
        return new ReplayListing(replays, hadRows: true, highestId: highest, nextAfter: highest);
    }
}
