using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayListCursorTests
{
    [Fact]
    public void AfterRejectedPage_AdvancesPastAVersionFilteredPage()
    {
        var page = new ReplayListing(
            playable: null,
            hadRows: true,
            highestId: 65270000,
            nextAfter: 65270010
        );

        Assert.Equal(65270010, ReplayListCursor.AfterRejectedPage(65267450, page));
    }

    [Fact]
    public void AfterRejectedPage_UsesTheHighestRowWhenNextAfterIsMissing()
    {
        var page = new ReplayListing(
            playable: null,
            hadRows: true,
            highestId: 65280000,
            nextAfter: null
        );

        Assert.Equal(65280000, ReplayListCursor.AfterRejectedPage(65267450, page));
    }

    [Fact]
    public void AfterRejectedPage_WaitsWhenTheApiPageIsEmpty()
    {
        Assert.Null(ReplayListCursor.AfterRejectedPage(65267450, ReplayListing.Empty));
        Assert.Null(
            ReplayListCursor.AfterRejectedPage(65267450, new ReplayListing(null, false, 0, null))
        );
    }

    [Fact]
    public void AfterRejectedPage_DoesNotSkipAPlayableReplay()
    {
        var page = new ReplayListing(
            new[]
            {
                new HeroesProfileReplay { Id = 65536854, GameVersion = "2.57.0.98285" },
            },
            hadRows: true,
            highestId: 65536854,
            nextAfter: 65536854
        );

        Assert.Null(ReplayListCursor.AfterRejectedPage(65536853, page));
    }
}
