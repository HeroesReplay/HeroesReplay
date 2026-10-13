using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData.Rotation;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesData.Rotation;

/// <summary>
/// #425: the Storm League map pool comes from the replays the download role keeps, so a map
/// Blizzard rotated out stops getting Storm League rewards.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class StormLeagueRotationTests
{
    private static readonly string[] Pool =
    {
        "Alterac Pass",
        "Battlefield of Eternity",
        "Braxis Holdout",
        "Cursed Hollow",
        "Dragon Shire",
        "Garden of Terror",
        "Haunted Mines",
        "Infernal Shrines",
        "Sky Temple",
        "Tomb of the Spider Queen",
        "Towers of Doom",
        "Volskaya Foundry",
    };

    private static readonly Map Hanamura = Standard("Hanamura Temple", ranked: true);
    private static readonly Map Cursed = Standard("Cursed Hollow", ranked: false);

    [Fact]
    public void NewestStormLeagueReplays_AreThePool()
    {
        StormLeagueRotation rotation = StormLeagueRotation.FromFileNames(
            SeasonOf(Pool, firstId: 65_000_000, perMap: 25)
        );

        Assert.True(rotation.Observed);
        // The cache, not the catalog flag, decides both ways.
        Assert.True(rotation.Contains(Cursed));
        Assert.False(rotation.Contains(Hanamura));
    }

    [Fact]
    public void TooFewCachedReplays_FallBackToTheCatalogFlag()
    {
        StormLeagueRotation rotation = StormLeagueRotation.FromFileNames(
            SeasonOf(Pool, firstId: 65_000_000, perMap: 8)
        );

        Assert.False(rotation.Observed);
        Assert.True(rotation.Contains(Hanamura));
        Assert.False(rotation.Contains(Cursed));
    }

    [Fact]
    public void OnlyTheNewestSampleCounts_SoALastSeasonMapDropsOut()
    {
        IEnumerable<string> lastSeason = SeasonOf(
            new[] { "Hanamura Temple", "Cursed Hollow" },
            firstId: 64_000_000,
            perMap: 200
        );
        IEnumerable<string> thisSeason = SeasonOf(Pool, firstId: 65_000_000, perMap: 30);

        StormLeagueRotation rotation = StormLeagueRotation.FromFileNames(
            lastSeason.Concat(thisSeason)
        );

        Assert.False(rotation.Contains(Hanamura));
        Assert.True(rotation.Contains(Cursed));
    }

    [Fact]
    public void QuickMatchFilesAndStrayGames_DoNotMakeAMapStormLeague()
    {
        List<string> files = SeasonOf(Pool, firstId: 65_000_000, perMap: 25).ToList();
        files.Add(Name(66_000_001, "Quick Match", "Hanamura Temple"));
        files.Add(Name(66_000_002, "Quick Match", "Hanamura Temple"));
        files.Add(Name(66_000_003, "Quick Match", "Hanamura Temple"));
        files.Add(Name(66_000_004, "Storm League", "Warhead Junction"));
        files.Add(Name(66_000_005, "Storm League", "Warhead Junction"));
        files.Add("notes.txt");
        files.Add("bad_name.StormReplay");

        StormLeagueRotation rotation = StormLeagueRotation.FromFileNames(files);

        Assert.False(rotation.Contains(Hanamura));
        Assert.False(rotation.Contains(Standard("Warhead Junction", ranked: true)));
    }

    [Fact]
    public void MapNames_AreCompared_AsTheCatalogWritesThem()
    {
        List<string> files = SeasonOf(Pool, firstId: 65_000_000, perMap: 25).ToList();
        for (int index = 0; index < 5; index++)
        {
            files.Add(Name(66_000_100 + index, "Storm League", "Blackheart’s Bay"));
        }

        StormLeagueRotation rotation = StormLeagueRotation.FromFileNames(files);

        Assert.True(rotation.Contains(Standard("Blackheart's Bay", ranked: false)));
    }

    [Fact]
    public void FromCache_ReadsTheStandardFolder_AndFallsBackWithoutOne()
    {
        string data = Path.Combine(
            Path.GetTempPath(),
            "hr-rotation-" + Guid.NewGuid().ToString("N")
        );
        var settings = new AppSettings
        {
            Location = new LocationSettings { DataDirectory = data },
            HeroesProfileApi = new HeroesProfileApiSettings
            {
                StandardCacheDirectoryName = "Standard",
            },
        };
        try
        {
            Assert.False(StormLeagueRotation.FromCache(settings).Observed);
            Assert.False(StormLeagueRotation.FromCache(new AppSettings()).Observed);
            Assert.False(StormLeagueRotation.FromCache(null).Observed);

            Directory.CreateDirectory(Path.Combine(data, "Standard"));
            foreach (string name in SeasonOf(Pool, firstId: 65_000_000, perMap: 10))
            {
                File.WriteAllBytes(Path.Combine(data, "Standard", name), Array.Empty<byte>());
            }

            StormLeagueRotation rotation = StormLeagueRotation.FromCache(settings);
            Assert.True(rotation.Observed);
            Assert.False(rotation.Contains(Hanamura));
        }
        finally
        {
            if (Directory.Exists(data))
            {
                Directory.Delete(data, recursive: true);
            }
        }
    }

    [Fact]
    public void Describe_NamesTheSourceThePoolAndTheMapsOutsideIt()
    {
        StormLeagueRotation rotation = StormLeagueRotation.FromFileNames(
            SeasonOf(Pool, firstId: 65_000_000, perMap: 25)
        );

        string line = rotation.Describe(
            new[]
            {
                Cursed,
                Hanamura,
                Standard("Warhead Junction", ranked: true),
                new Map("Silver City", "SilverCity", false, "ARAM", true),
            }
        );

        Assert.Equal(
            "Storm League map pool from the newest 300 cached Storm League replays: Cursed Hollow. Not in Storm League: Hanamura Temple, Warhead Junction.",
            line
        );
    }

    private static Map Standard(string name, bool ranked) =>
        new(name, name.Replace(" ", string.Empty), ranked, "standard", true);

    private static IEnumerable<string> SeasonOf(IReadOnlyList<string> maps, int firstId, int perMap)
    {
        int id = firstId;
        for (int round = 0; round < perMap; round++)
        {
            foreach (string map in maps)
            {
                yield return Name(id++, "Storm League", map);
            }
        }
    }

    private static string Name(int id, string gameType, string map) =>
        $"{id}_{gameType}_Gold 3_{map}_0a1b2c3d-0000-0000-0000-000000000000_.StormReplay";
}
