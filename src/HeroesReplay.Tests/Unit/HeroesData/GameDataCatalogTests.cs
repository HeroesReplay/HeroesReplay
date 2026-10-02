using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.HeroesData;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class GameDataCatalogTests
{
    [Fact]
    public void Archive_PrefersTheHeroesData2NoMapsZip()
    {
        using JsonDocument document = JsonDocument.Parse(
            """
            {
              "name": "v2.57.0.98304",
              "zipball_url": "https://api.github.com/repos/HeroesToolChest/heroes-data2/zipball/v2.57.0.98304",
              "assets": [
                {
                  "name": "heroes-data-2.57.0.98304.zip",
                  "browser_download_url": "https://github.com/HeroesToolChest/heroes-data2/releases/download/v2.57.0.98304/heroes-data-2.57.0.98304.zip"
                },
                {
                  "name": "heroes-data-no-maps-2.57.0.98304.zip",
                  "browser_download_url": "https://github.com/HeroesToolChest/heroes-data2/releases/download/v2.57.0.98304/heroes-data-no-maps-2.57.0.98304.zip"
                },
                {
                  "name": "heroes-data-no-maps-2.57.0.98304.tar.gz",
                  "browser_download_url": "https://github.com/HeroesToolChest/heroes-data2/releases/download/v2.57.0.98304/heroes-data-no-maps-2.57.0.98304.tar.gz"
                }
              ]
            }
            """
        );

        Assert.True(
            GameData.TrySelectArchive(document.RootElement, out GameData.HeroesDataArchive archive)
        );
        Assert.Equal("heroes-data-no-maps-2.57.0.98304.zip", archive.FileName);
        Assert.Equal(98304, GameData.BuildNumber(archive.FileName));
        Assert.Equal(98304, GameData.BuildNumber("herodata_98304.json"));
        Assert.Equal(96370, GameData.BuildNumber("herodata_96370_localized.json"));
    }

    [Fact]
    public void AppSettings_PointsAtHeroesData2()
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

        Assert.Contains(
            "https://api.github.com/repos/HeroesToolChest/heroes-data2/releases/latest",
            text,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("repos/HeroesToolChest/heroes-data/", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_ReadsHeroesData2RolesReleaseDateAndUnits()
    {
        string parent = Path.Combine(
            Path.GetTempPath(),
            "hr-catalog-" + Guid.NewGuid().ToString("N")
        );
        string cache = Path.Combine(parent, "HeroesData");
        Directory.CreateDirectory(cache);
        try
        {
            File.WriteAllText(
                Path.Combine(cache, "herodata_96370_localized.json"),
                "{\"OldHero\":{\"unitId\":\"HeroOld\"}}"
            );
            File.WriteAllText(Path.Combine(cache, "herodata_98304.json"), HeroData);
            File.WriteAllText(Path.Combine(cache, "unitdata_98304.json"), UnitData);
            File.WriteAllText(Path.Combine(cache, "gamestrings_98304_enus.json"), GameStrings);
            File.WriteAllText(
                Path.Combine(cache, "gamestrings_mapdata_98304_enus.json"),
                "{\"meta\":{\"itemsType\":\"GameStrings\"},\"items\":{}}"
            );

            GameData data = new GameData(NullLogger<GameData>.Instance, Settings(parent));
            await data.LoadDataAsync();

            Hero xalatath = data.Heroes.Single(hero => hero.HyperlinkId == "Xalatath");
            Hero johanna = data.Heroes.Single(hero => hero.Name == "Johanna");

            Assert.Equal(2, data.Heroes.Count);
            Assert.Equal("Xal'atath", xalatath.Name);
            Assert.Equal("HeroXalatath", xalatath.UnitId);
            Assert.Equal("HXAL", xalatath.AttributeId);
            Assert.Equal("Ranged Assassin", xalatath.Role);
            Assert.Equal(
                new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc),
                xalatath.ReleaseDate
            );
            Assert.Contains("Ganker", xalatath.Descriptors);
            Assert.False(xalatath.IsMelee);
            Assert.Equal(8, xalatath.Ratings.Damage);
            Assert.Equal(6, xalatath.Ratings.Survivability);
            Assert.True(johanna.IsMelee);
            Assert.Equal(10, johanna.Ratings.Survivability);
            Assert.Equal("Tank", johanna.Role);
            Assert.Equal("Johanna", johanna.HyperlinkId);
            Assert.Equal(UnitGroup.Hero, data.GetUnitGroup("HeroXalatath"));
            Assert.Equal(UnitGroup.Hero, data.GetUnitGroup("XalatathVoidMinion"));
            Assert.Contains("KingsCore", data.CoreUnits);
            Assert.Contains("VehicleDragon", data.VehicleUnits);
            Assert.Equal(UnitGroup.Structures, data.GetUnitGroup("KingsCore"));
            Assert.Equal(UnitGroup.MapObjective, data.GetUnitGroup("VehicleDragon"));
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Fact]
    public async Task Load_ReadsTheExtractedHeroesData2CatalogWhenPresent()
    {
        string extract = Path.Combine(Path.GetTempPath(), "heroes-data2-2.57.0.98304", "extract");
        string heroFile = Path.Combine(extract, "data", "herodata_98304.json");
        string unitFile = Path.Combine(extract, "data", "unitdata_98304.json");
        string stringsFile = Path.Combine(extract, "gamestrings", "gamestrings_98304_enus.json");
        if (!File.Exists(heroFile) || !File.Exists(unitFile) || !File.Exists(stringsFile))
        {
            return;
        }

        string parent = Path.Combine(Path.GetTempPath(), "hr-real-" + Guid.NewGuid().ToString("N"));
        string cache = Path.Combine(parent, "HeroesData");
        Directory.CreateDirectory(cache);
        try
        {
            File.Copy(heroFile, Path.Combine(cache, "herodata_98304.json"));
            File.Copy(unitFile, Path.Combine(cache, "unitdata_98304.json"));
            File.Copy(stringsFile, Path.Combine(cache, "gamestrings_98304_enus.json"));

            GameData data = new GameData(NullLogger<GameData>.Instance, Settings(parent));
            await data.LoadDataAsync();

            Hero xalatath = data.Heroes.Single(hero => hero.AttributeId == "HXAL");
            Hero johanna = data.Heroes.Single(hero => hero.HyperlinkId == "Johanna");
            Assert.Equal("Xal'atath", xalatath.Name);
            Assert.Equal("HeroXalatath", xalatath.UnitId);
            Assert.Equal("Ranged Assassin", xalatath.Role);
            Assert.Equal(
                new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc),
                xalatath.ReleaseDate
            );
            Assert.Equal("Johanna", johanna.Name);
            Assert.Equal("Tank", johanna.Role);
            Assert.Contains("KingsCore", data.CoreUnits);
            Assert.True(data.Heroes.Count >= 90);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    private static AppSettings Settings(string dataDirectory)
    {
        return new AppSettings
        {
            Location = new LocationSettings { DataDirectory = dataDirectory },
            HeroesToolChest = new HeroesToolChestSettings
            {
                HeroesDataReleaseUri = new Uri(
                    "https://api.github.com/repos/HeroesToolChest/heroes-data2/releases/latest"
                ),
                IgnoreUnits = new[] { "Dummy" },
                ObjectiveContains = new[] { "RavenLordTribute" },
                BossContains = new[] { "UnderworldBoss" },
                CampContains = new[] { "MercLanerMeleeKnight" },
                VehicleContains = new[] { "GardenTerror" },
                CaptureContains = Array.Empty<string>(),
                CoreScalingLinkId = "CoreScaling",
                VehicleScalingLinkIds = new[] { "DragonKnightScaling" },
            },
        };
    }

    private const string HeroData = """
        {
          "meta": {
            "heroesVersion": "2.57.0.98304",
            "hdpVersion": "5.1.0",
            "itemsType": "Data",
            "dataType": "HeroData",
            "localizedText": "Extracted",
            "totalItems": 2
          },
          "items": {
            "Xalatath": {
              "unitId": "HeroXalatath",
              "hyperlinkId": "Xalatath",
              "attributeId": "HXAL",
              "releaseDate": "2026-09-28",
              "isMelee": false,
              "ratings": { "complexity": 5, "damage": 8, "survivability": 6, "utility": 6 },
              "attributes": ["Heroic"],
              "scalingLinkIds": ["HeroDummyVeterancy"],
              "playstyles": ["Ganker"],
              "heroUnits": {
                "XalatathVoidMinion": { "radius": 0.5 }
              }
            },
            "Crusader": {
              "unitId": "HeroCrusader",
              "hyperlinkId": "Johanna",
              "attributeId": "Crus",
              "releaseDate": "2015-06-02",
              "isMelee": true,
              "ratings": { "complexity": 2, "damage": 3, "survivability": 10, "utility": 6 },
              "attributes": ["Heroic"],
              "playstyles": ["RoleTank"]
            }
          }
        }
        """;

    private const string UnitData = """
        {
          "meta": {
            "heroesVersion": "2.57.0.98304",
            "hdpVersion": "5.1.0",
            "itemsType": "Data",
            "dataType": "UnitData",
            "localizedText": "Extracted",
            "totalItems": 2
          },
          "items": {
            "KingsCore": {
              "attributes": ["AITargetableStructure", "Structure"],
              "scalingLinkIds": ["CoreScaling"]
            },
            "VehicleDragon": {
              "attributes": ["Heroic"],
              "scalingLinkIds": ["DragonKnightScaling"],
              "playstyles": ["PowerfulLaner"]
            }
          }
        }
        """;

    private const string GameStrings = """
        {
          "meta": {
            "heroesVersion": "2.57.0.98304",
            "hdpVersion": "5.1.0",
            "itemsType": "GameStrings",
            "dataTypes": ["HeroData", "UnitData"],
            "gameStringText": { "locale": "ENUS" }
          },
          "items": {
            "hero": {
              "name": { "Xalatath": "Xal'atath", "Crusader": "Johanna" },
              "expandedRole": { "Xalatath": "Ranged Assassin", "Crusader": "Tank" }
            }
          }
        }
        """;
}
