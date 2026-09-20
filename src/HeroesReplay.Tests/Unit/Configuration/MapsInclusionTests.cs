using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace HeroesReplay.Tests.Unit.Configuration;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MapsInclusionTests
{
    private static readonly string[] HistoryMaps =
    {
        "Alterac Pass",
        "Battlefield of Eternity",
        "Blackheart's Bay",
        "Braxis Holdout",
        "Braxis Outpost",
        "Cursed Hollow",
        "Dragon Shire",
        "Escape From Braxis",
        "Escape From Braxis (Heroic)",
        "Garden of Terror",
        "Hanamura Temple",
        "Haunted Mines",
        "Industrial District",
        "Infernal Shrines",
        "Lost Cavern",
        "Pull Party",
        "Silver City",
        "Sky Temple",
        "Snow Brawl",
        "Tomb of the Spider Queen",
        "Towers of Doom",
        "Volskaya Foundry",
        "Warhead Junction",
    };

    [Fact]
    public void AppSettings_IncludesEveryMapFromReplayHistoryAsPlayable()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement catalog = document.RootElement.GetProperty("Maps").GetProperty("Catalog");

        var byName = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement map in catalog.EnumerateArray())
        {
            byName[map.GetProperty("Name").GetString()] = map;
        }

        foreach (string name in HistoryMaps)
        {
            Assert.True(
                byName.ContainsKey(name),
                $"Maps:Catalog is missing '{name}' from replay history."
            );
            Assert.True(byName[name].GetProperty("Playable").GetBoolean());
        }
    }
}
