using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Services.Analysis.Reports;

public sealed class CalculatorUnitLists
{
    public IReadOnlyList<string> ObjectiveContains { get; set; } = new string[0];

    public IReadOnlyList<string> BossContains { get; set; } = new string[0];

    public IReadOnlyList<string> CampContains { get; set; } = new string[0];

    public IReadOnlyList<string> VehicleContains { get; set; } = new string[0];

    public IReadOnlyList<string> CaptureContains { get; set; } = new string[0];

    public IReadOnlyList<string> IgnoreContains { get; set; } = new string[0];

    public static bool Matches(IReadOnlyList<string> needles, string name)
    {
        if (needles == null || string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (string needle in needles)
        {
            if (
                !string.IsNullOrWhiteSpace(needle)
                && name.Contains(needle, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }
}

public static class UnitInterest
{
    public static IReadOnlyList<string> Tags(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new string[0];
        }

        var tags = new List<string>();
        if (
            Contains(
                name,
                "WatchTower",
                "Watchtower",
                "Revealer",
                "ScoutingDrone",
                "Farsight",
                "Clairvoyance"
            )
        )
        {
            tags.Add("vision");
        }

        if (Contains(name, "Merc", "Sapper", "SiegeGiant", "Hellbat", "Bruiser", "Camp"))
        {
            tags.Add("camp");
        }

        if (Contains(name, "Boss"))
        {
            tags.Add("boss");
        }

        if (
            name.StartsWith("Item", StringComparison.OrdinalIgnoreCase)
            || Contains(
                name,
                "Medpack",
                "MedPack",
                "RegenGlobe",
                "HealingPulse",
                "HealingPotion",
                "Pickup"
            )
            || (
                Contains(name, "Turret")
                && !name.StartsWith("Town", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            tags.Add("item");
        }

        if (
            Contains(
                name,
                "DragonKnight",
                "Dragon",
                "Triglav",
                "GardenTerror",
                "Protector",
                "Punisher",
                "Vehicle"
            )
        )
        {
            tags.Add("vehicle");
        }

        if (Contains(name, "Core"))
        {
            tags.Add("core");
        }

        if (
            Contains(
                name,
                "Objective",
                "Tribute",
                "Curse",
                "Shrine",
                "Altar",
                "Beacon",
                "Payload",
                "Warhead",
                "Nuke",
                "Seed",
                "Gem",
                "Skull",
                "Cage",
                "Chest",
                "Captain"
            )
        )
        {
            tags.Add("objective");
        }

        return tags;
    }

    public static string CalculatorLists(string name, CalculatorUnitLists lists)
    {
        if (lists == null || string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var hits = new List<string>();
        if (CalculatorUnitLists.Matches(lists.ObjectiveContains, name))
        {
            hits.Add("objective");
        }

        if (CalculatorUnitLists.Matches(lists.BossContains, name))
        {
            hits.Add("boss");
        }

        if (CalculatorUnitLists.Matches(lists.CampContains, name))
        {
            hits.Add("camp");
        }

        if (CalculatorUnitLists.Matches(lists.VehicleContains, name))
        {
            hits.Add("vehicle");
        }

        if (CalculatorUnitLists.Matches(lists.CaptureContains, name))
        {
            hits.Add("capture");
        }

        return string.Join(";", hits);
    }

    public static bool IsCandidate(string name, string parserGroup, CalculatorUnitLists lists)
    {
        if (CalculatorUnitLists.Matches(lists?.IgnoreContains, name))
        {
            return false;
        }

        if (Tags(name).Count > 0 || !string.IsNullOrEmpty(CalculatorLists(name, lists)))
        {
            return true;
        }

        if (name.StartsWith("Hero", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return parserGroup == "MapObjective"
            || parserGroup == "MercenaryCamp"
            || parserGroup == "Miscellaneous"
            || parserGroup == "Unknown";
    }

    private static bool Contains(string name, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (name.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
