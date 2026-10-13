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
    public void HeroesProfileZero_TakesAMapOut_ThoughCachedGamesAreOnIt()
    {
        StormLeagueRotation rotation = StormLeagueRotation
            .FromFileNames(SeasonOf(Pool, firstId: 65_000_000, perMap: 25))
            .With(Flags(("Cursed Hollow", 0), ("Sky Temple", 1)));

        Assert.False(rotation.Contains(Cursed));
        Assert.True(rotation.Contains(Standard("Sky Temple", ranked: false)));
    }

    [Fact]
    public void HeroesProfileOne_StillNeedsCachedStormLeagueGames()
    {
        // 2026-10: Heroes Profile flagged Hanamura Temple and Warhead Junction, and none of
        // production's 1,265 Storm League downloads were on them.
        StormLeagueRotation rotation = StormLeagueRotation
            .FromFileNames(SeasonOf(Pool, firstId: 65_000_000, perMap: 25))
            .With(
                Flags(
                    ("Hanamura Temple", 1),
                    ("Warhead Junction", 1),
                    ("Blackheart's Bay", 0),
                    ("Cursed Hollow", 1)
                )
            );

        Assert.False(rotation.Contains(Hanamura));
        Assert.False(rotation.Contains(Standard("Warhead Junction", ranked: true)));
        Assert.False(rotation.Contains(Standard("Blackheart's Bay", ranked: true)));
        Assert.True(rotation.Contains(Cursed));
    }

    [Fact]
    public void TooFewCachedReplays_HeroesProfileFlagDecides_ThenTheCatalog()
    {
        StormLeagueRotation rotation = StormLeagueRotation
            .FromFileNames(SeasonOf(Pool, firstId: 65_000_000, perMap: 8))
            .With(Flags(("Hanamura Temple", 0), ("Cursed Hollow", 1)));

        Assert.False(rotation.Observed);
        Assert.True(rotation.Flagged);
        Assert.False(rotation.Contains(Hanamura));
        Assert.True(rotation.Contains(Cursed));
        // Heroes Profile does not list it, so the catalog flag decides.
        Assert.True(rotation.Contains(Standard("Alterac Pass", ranked: true)));
        Assert.False(rotation.Contains(Standard("Sky Temple", ranked: false)));
    }

    [Fact]
    public void HeroesProfileNames_MatchTheCatalogNames()
    {
        StormLeagueRotation rotation = StormLeagueRotation.Catalog.With(
            Flags(("Blackheart’s Bay", 1), ("  cursed hollow ", 0))
        );

        Assert.True(rotation.Contains(Standard("Blackheart's Bay", ranked: false)));
        Assert.False(rotation.Contains(Standard("Cursed Hollow", ranked: true)));
        Assert.False(StormLeagueRotation.Catalog.Flagged);
    }

    [Fact]
    public void Describe_BothSources_NamesTheFlaggedMapsNoGameIsOn()
    {
        StormLeagueRotation rotation = StormLeagueRotation
            .FromFileNames(SeasonOf(Pool, firstId: 65_000_000, perMap: 25))
            .With(Flags(("Cursed Hollow", 1), ("Hanamura Temple", 1), ("Blackheart's Bay", 0)));

        string line = rotation.Describe(
            new[] { Cursed, Hanamura, Standard("Blackheart's Bay", ranked: false) }
        );

        Assert.Equal(
            "Storm League map pool from Heroes Profile ranked_rotation (2026-10-13) and the newest 300 cached Storm League replays: Cursed Hollow. Not in Storm League: Hanamura Temple, Blackheart's Bay. Heroes Profile marks Hanamura Temple ranked, but no cached Storm League replay is on them.",
            line
        );
    }

    [Fact]
    public void FromCache_ReadsTheSavedHeroesProfileMapList()
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
            Assert.False(StormLeagueRotation.FromCache(settings).Flagged);

            HeroesProfileMapStore.For(settings).Write(Flags(("Hanamura Temple", 0)));
            StormLeagueRotation rotation = StormLeagueRotation.FromCache(settings);

            Assert.True(rotation.Flagged);
            Assert.False(rotation.Observed);
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

    private static HeroesProfileMapList Flags(params (string Name, int Ranked)[] maps) =>
        new(
            new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero),
            maps.Select(map => new HeroesProfileMap(map.Name, "standard", map.Ranked == 1, true))
                .ToList()
        );

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
