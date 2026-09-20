using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Heroes.ReplayParser;
using Xunit;
using Xunit.Abstractions;

namespace HeroesReplay.Tests.Unit.Analysis;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HauntedMinesMapTests
{
    private const string ReplayPath =
        @"C:\Users\patri\Documents\Heroes of the Storm\Accounts\1193364618\2-Hero-1-14343183\Replays\Multiplayer\2026-09-16 18.19.03 Haunted Mines.StormReplay";

    private readonly ITestOutputHelper output;

    public HauntedMinesMapTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void AppSettings_MarksHauntedMinesPlayable()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement haunted = document
            .RootElement.GetProperty("Maps")
            .GetProperty("Catalog")
            .EnumerateArray()
            .Single(map => map.GetProperty("Name").GetString() == "Haunted Mines");

        Assert.Equal("HauntedMines", haunted.GetProperty("ShortName").GetString());
        Assert.True(haunted.GetProperty("Playable").GetBoolean());
        Assert.True(haunted.GetProperty("RankedRotation").GetBoolean());
    }

    [Fact]
    public void AppSettings_IncludesHauntedMinesUnitsAndCarriedObjective()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement maps = document
            .RootElement.GetProperty("Maps")
            .GetProperty("CarriedObjectives");
        var carried = maps.EnumerateArray().Select(e => e.GetString()).ToHashSet();
        Assert.Contains("Haunted Mines", carried);
        Assert.Contains("HauntedMines", carried);

        JsonElement chest = document.RootElement.GetProperty("HeroesToolChest");
        Assert.Contains(
            chest.GetProperty("BossContains").EnumerateArray().Select(e => e.GetString()),
            n => n == "UnderworldBoss"
        );
        Assert.Contains(
            chest.GetProperty("CampContains").EnumerateArray().Select(e => e.GetString()),
            n => n == "UnderworldMinion"
        );
        Assert.Contains(
            chest.GetProperty("VehicleContains").EnumerateArray().Select(e => e.GetString()),
            n => n == "UnderworldSummonedBoss"
        );
        Assert.Contains(
            chest.GetProperty("ObjectiveContains").EnumerateArray().Select(e => e.GetString()),
            n => n == "ItemUnderworldPowerup"
        );
    }

    [Fact]
    public void SampleReplay_ParsesAsHauntedMinesWithCampsAndBossUnits()
    {
        if (!File.Exists(ReplayPath))
        {
            return;
        }

        var result = DataParser.ParseReplay(
            File.ReadAllBytes(ReplayPath),
            new ParseOptions
            {
                ShouldParseEvents = true,
                ShouldParseUnits = true,
                ShouldParseStatistics = true,
            }
        );

        Replay replay = result.Item2;
        Assert.NotNull(replay);
        Assert.Equal("Haunted Mines", replay.Map);
        output.WriteLine(
            $"Map={replay.Map} Alt={replay.MapAlternativeName} Length={replay.ReplayLength}"
        );

        var names = replay
            .Units.Select(u => u.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        IEnumerable<string> interesting = names.Where(n =>
            n.Contains("Golem", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Skull", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Mine", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Underworld", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Merc", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Camp", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Miner", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Sapper", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Giant", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Grave", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Boss", StringComparison.OrdinalIgnoreCase)
        );

        foreach (string name in interesting)
        {
            output.WriteLine(name);
        }

        Assert.Contains("UnderworldBoss", names);
        Assert.Contains("UnderworldMinion", names);
        Assert.Contains("UnderworldSummonedBoss", names);
        Assert.Contains("MercDefenderSiegeGiant", names);
        Assert.Contains("MercGoblinSapperDefender", names);
        Assert.Contains("ItemUnderworldPowerup", names);
    }
}
