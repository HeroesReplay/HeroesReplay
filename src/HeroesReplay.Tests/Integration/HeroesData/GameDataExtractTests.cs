using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Shared;
using HeroesReplay.Tests.Unit.HeroesData;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Integration.HeroesData;

/// <summary>
/// Loads a real heroes-data2 2.57.0.98304 extract when one is on this machine:
/// <c>HEROESREPLAY_HEROES_DATA2_EXTRACT</c>, else <c>%TEMP%\heroes-data2-2.57.0.98304\extract</c>.
/// It was a unit test that read that shared temp folder (#331), so whether it ran depended on
/// what another process had left there.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Integration)]
public class GameDataExtractTests
{
    [Fact]
    public async Task Load_ReadsTheExtractedHeroesData2CatalogWhenPresent()
    {
        string extract =
            Environment.GetEnvironmentVariable("HEROESREPLAY_HEROES_DATA2_EXTRACT")
            ?? Path.Combine(Path.GetTempPath(), "heroes-data2-2.57.0.98304", "extract");
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

            GameData data = new GameData(
                NullLogger<GameData>.Instance,
                GameDataCatalogTests.Settings(parent)
            );
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
}
