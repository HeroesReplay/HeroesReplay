using System.Collections.Generic;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Queue;
using Xunit;

namespace HeroesReplay.Tests.Unit.Queue;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class RewardCandidateFilterTests
{
    [Fact]
    public void Choose_SkipsPlayedQueuedAndOldVersions()
    {
        var replays = new[]
        {
            new HeroesProfileReplay
            {
                Id = 1,
                GameVersion = "2.55.17.98025",
                Map = "Cursed Hollow",
            },
            new HeroesProfileReplay
            {
                Id = 2,
                GameVersion = "2.55.17.97650",
                Map = "Sky Temple",
            },
            new HeroesProfileReplay
            {
                Id = 3,
                GameVersion = "2.55.17.98025",
                Map = "Dragon Shire",
            },
            new HeroesProfileReplay
            {
                Id = 4,
                GameVersion = "1.0.0.1",
                Map = "Old",
            },
        };

        HeroesProfileReplay chosen = RewardCandidateFilter.Choose(
            replays,
            new HashSet<int> { 1 },
            new HashSet<int> { 2 },
            new[] { "2.55.17.98025", "2.55.17.97650" }
        );

        Assert.NotNull(chosen);
        Assert.Equal(3, chosen.Id);
    }
}
