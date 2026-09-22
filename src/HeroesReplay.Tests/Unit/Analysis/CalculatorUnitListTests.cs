using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.Services.Analysis.Reports;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class CalculatorUnitListTests
{
    [Fact]
    public void Issue32Units_MatchTheCalculatorLists()
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
        );
        JsonElement root = document.RootElement;
        JsonElement chest = root.GetProperty("HeroesToolChest");
        JsonElement focus = root.GetProperty("FocusUnits");

        Assert.True(Matches(focus, "CoreContains", "VanndarStormpike"));
        Assert.True(Matches(focus, "CoreContains", "DrekThar"));
        Assert.True(Matches(focus, "PickupContains", "RegenGlobeNeutral"));
        Assert.True(Matches(focus, "ObjectiveContains", "DragonShireShrineSun"));
        Assert.True(
            Matches(focus, "ObjectiveContains", "ZergHiveControlBeacon")
                || Matches(chest, "CaptureContains", "ZergHiveControlBeacon")
        );
        Assert.True(Matches(focus, "ObjectiveContains", "ZergUltralisk"));
        Assert.True(Matches(chest, "VehicleContains", "VehiclePlantHorror"));
        Assert.True(Matches(chest, "BossContains", "BossDuelLanerHeaven"));
        Assert.True(Matches(chest, "BossContains", "TerranArchangelLaner"));
        Assert.True(Matches(chest, "BossContains", "JungleGraveGolemLaner"));
        Assert.True(Matches(chest, "BossContains", "SoulEater"));
        Assert.True(Matches(focus, "CampContains", "MercLanerSiegeGiant"));
        Assert.True(Matches(focus, "CampContains", "TerranGoliath"));
        Assert.True(Matches(focus, "CampContains", "MercSummonerLanerMinion"));
        Assert.Contains(
            "HauntedWoodsVehicleScaling",
            Names(chest.GetProperty("VehicleScalingLinkIds"))
        );
        Assert.True(Matches(chest, "IgnoreUnits", "NukeTargetMinimapIconUnit"));
        Assert.True(Matches(chest, "IgnoreUnits", "ZergPathDummy"));
    }

    private static bool Matches(JsonElement owner, string property, string unit) =>
        CalculatorUnitLists.Matches(Names(owner.GetProperty(property)), unit);

    private static List<string> Names(JsonElement array)
    {
        var names = new List<string>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            names.Add(item.GetString());
        }

        return names;
    }
}
