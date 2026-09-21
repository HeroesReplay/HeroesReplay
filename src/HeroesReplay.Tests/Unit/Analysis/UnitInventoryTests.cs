using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.CLI.Commands.Calculators.Commands;
using HeroesReplay.Core.Services.Analysis.Reports;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class UnitInventoryTests
{
    [Fact]
    public void Identity_MergesLocalizedTitlesByUniqueObjectiveUnits()
    {
        var known = new Dictionary<string, IReadOnlyCollection<string>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["Cursed Hollow"] = new[]
            {
                "RavenLordTribute",
                "RavenLordTributeWarning",
                "MercDefenderSiegeGiant",
            },
            ["Dragon Shire"] = new[]
            {
                "VehicleDragon",
                "DragonShireShrineMoon",
                "MercDefenderSiegeGiant",
            },
        };

        IReadOnlyDictionary<string, string> markers = MapIdentity.UniqueMarkers(known);
        Assert.Equal("Cursed Hollow", markers["RavenLordTribute"]);
        Assert.False(markers.ContainsKey("MercDefenderSiegeGiant"));
        Assert.Equal(
            "Cursed Hollow",
            MapIdentity.Match(
                new[] { "RavenLordTribute", "RavenLordTributeWarning", "HeroJaina" },
                markers,
                "诅咒谷"
            )
        );
        Assert.Equal("诅咒谷", MapIdentity.Match(new[] { "RavenLordTribute" }, markers, "诅咒谷"));
        Assert.Equal("巨龙镇", MapIdentity.Match(new[] { "StormPig" }, markers, "巨龙镇"));
        Assert.True(MapIdentity.CanIdentifyMap("RavenLordTribute", "MapObjective"));
        Assert.False(MapIdentity.CanIdentifyMap("HeroJaina", "Unknown"));
        Assert.False(MapIdentity.CanIdentifyMap("TownGateL2", "Structures"));
        Assert.False(MapIdentity.CanIdentifyMap("FootmanMinion", "Minions"));
    }

    [Fact]
    public void Sampler_KeepsTheFirstReplaysForEachMap()
    {
        var sampler = new MapReplaySampler(2);
        Assert.True(sampler.TryTake("Tomb of the Spider Queen", @"C:\replays\a.StormReplay"));
        Assert.True(sampler.TryTake("tomb of the spider queen", @"C:\replays\b.StormReplay"));
        Assert.False(sampler.TryTake("Tomb of the Spider Queen", @"C:\replays\c.StormReplay"));
        Assert.True(sampler.TryTake("Haunted Mines", @"C:\replays\d.StormReplay"));

        MapReplaySample tomb = sampler
            .Selected()
            .Single(sample => sample.Map == "Tomb of the Spider Queen");
        Assert.Equal(3, tomb.FilesIdentified);
        Assert.Equal(
            new[] { @"C:\replays\a.StormReplay", @"C:\replays\b.StormReplay" },
            tomb.Paths.ToArray()
        );
        Assert.Equal(2, sampler.Selected().Count);
    }

    [Fact]
    public void Inventory_CountsOccurrencesAndReplayCoverage()
    {
        var inventory = new UnitInventory();
        inventory.AddReplay(
            "Haunted Mines",
            "2.55.17.98025",
            new (string, string)[]
            {
                ("UnderworldBoss", "MercenaryCamp"),
                ("UnderworldBoss", "MercenaryCamp"),
                ("Footman", "Minions"),
            }
        );
        inventory.AddReplay(
            "Haunted Mines",
            "2.55.17.97771",
            new (string, string)[] { ("Footman", "Minions") }
        );

        UnitInventoryRow boss = inventory.Rows().Single(row => row.Name == "UnderworldBoss");
        Assert.Equal(2, boss.Occurrences);
        Assert.Equal(1, boss.ReplaysWithUnit);
        Assert.Equal(2, boss.ReplaysSampled);
        Assert.Equal("2.55.17.98025", boss.ReplayVersions);
        Assert.Equal("MercenaryCamp", boss.ParserGroup);

        UnitInventoryRow footman = inventory.Rows().Single(row => row.Name == "Footman");
        Assert.Equal(2, footman.ReplaysWithUnit);
        Assert.Equal("2.55.17.97771;2.55.17.98025", footman.ReplayVersions);
    }

    [Fact]
    public void Interest_TagsUnitsCalculatorsShouldNotice()
    {
        Assert.Contains("item", UnitInterest.Tags("ItemUnderworldPowerup"));
        Assert.Contains("boss", UnitInterest.Tags("UnderworldBoss"));
        Assert.Contains("camp", UnitInterest.Tags("MercDefenderSiegeGiant"));
        Assert.Contains("vision", UnitInterest.Tags("WatchTower"));
        Assert.Contains("vehicle", UnitInterest.Tags("DragonKnight"));
        Assert.Contains("vehicle", UnitInterest.Tags("Triglav"));
        Assert.Contains("core", UnitInterest.Tags("StormCore"));
        Assert.Contains("item", UnitInterest.Tags("RegenGlobe"));
        Assert.Contains("item", UnitInterest.Tags("DeckardHealingPotion"));
        Assert.Contains("vision", UnitInterest.Tags("ScoutingDrone"));
        Assert.Contains("item", UnitInterest.Tags("TinkerRockItTurret"));
        Assert.DoesNotContain("item", UnitInterest.Tags("TownTurretDummy"));
        Assert.DoesNotContain("vision", UnitInterest.Tags("TownGateL2VerticalLeftVisionBlocked"));
        Assert.Empty(UnitInterest.Tags("HeroJaina"));

        var lists = new CalculatorUnitLists
        {
            ObjectiveContains = new[] { "ItemUnderworldPowerup" },
            BossContains = new[] { "UnderworldBoss" },
            CaptureContains = new[] { "CaptureBeacon" },
            IgnoreContains = new[] { "Dummy", "IconUnit", "PathingBlocker", "LootBannerSconce" },
        };

        Assert.Equal("objective", UnitInterest.CalculatorLists("ItemUnderworldPowerup", lists));
        Assert.Equal("boss", UnitInterest.CalculatorLists("UnderworldBoss", lists));
        Assert.Equal(string.Empty, UnitInterest.CalculatorLists("WatchTower", lists));
        Assert.True(UnitInterest.IsCandidate("WatchTower", "Structures", lists));
        Assert.True(UnitInterest.IsCandidate("LoosePickup", "Miscellaneous", lists));
        Assert.True(UnitInterest.IsCandidate("RegenGlobe", "HeroAbilityUse", lists));
        Assert.False(UnitInterest.IsCandidate("HeroJaina", "Hero", lists));
        Assert.False(UnitInterest.IsCandidate("HeroDeckard", "Unknown", lists));
        Assert.False(
            UnitInterest.IsCandidate("MercDefenderSiegeGiantOrientDummy", "Unknown", lists)
        );
        Assert.False(UnitInterest.IsCandidate("FootmanMinion", "Minions", lists));
    }

    [Fact]
    public void Writer_QuotesCellsAndWritesCandidateRows()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-units-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            var sampler = new MapReplaySampler(1);
            sampler.TryTake("Blackheart's Bay", @"C:\replays\one.StormReplay");
            var inventory = new UnitInventory();
            inventory.AddReplay(
                "Blackheart's Bay",
                "2.55",
                new (string, string)[]
                {
                    ("Item, \"Cannonball\"", "MapObjective"),
                    ("Footman", "Minions"),
                }
            );

            UnitReportWriter.Write(
                directory,
                sampler.Selected(),
                new List<ReplaySampleRow>
                {
                    new ReplaySampleRow
                    {
                        Map = "Blackheart's Bay",
                        Path = @"C:\replays\one.StormReplay",
                    },
                },
                inventory.Rows(),
                new List<ReplayFailureRow>(),
                new CalculatorUnitLists { ObjectiveContains = new[] { "Cannonball" } }
            );

            string units = File.ReadAllText(Path.Combine(directory, "units-by-map.csv"));
            Assert.Contains("\"Item, \"\"Cannonball\"\"\"", units);
            string candidates = File.ReadAllText(Path.Combine(directory, "candidates.csv"));
            Assert.Contains("Cannonball", candidates);
            Assert.DoesNotContain("Footman", candidates);
            Assert.Contains(
                "Blackheart's Bay,1,1",
                File.ReadAllText(Path.Combine(directory, "survey-maps.csv"))
            );
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadCalculatorLists_ReadsHeroesToolChestNames()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-settings-" + Guid.NewGuid().ToString("N") + ".json"
        );
        try
        {
            File.WriteAllText(
                path,
                """
                { "HeroesToolChest": { "BossContains": [ "UnderworldBoss" ], "CampContains": [ "UnderworldMinion" ] } }
                """
            );

            CalculatorUnitLists lists = UnitsCommand.LoadCalculatorLists(path);
            Assert.Equal("boss", UnitInterest.CalculatorLists("UnderworldBoss", lists));
            Assert.Equal("camp", UnitInterest.CalculatorLists("UnderworldMinion", lists));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
