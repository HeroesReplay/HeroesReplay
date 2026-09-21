using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Analysis;
using HeroesReplay.Core.Services.Analysis.Calculators;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ReplayUnit = Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.Analysis;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class NearMapUnitCalculatorTests
{
    [Fact]
    public void HeroWithinRange_IsFocusedOnTheTribute()
    {
        Player hero = Hero("Valla", 0, 0);
        ReplayUnit tribute = Moving("RavenLordTribute", 5, 3, 4);
        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(Replay(hero, tribute), Settings());

        Assert.True(focus.TryGetValue(TimeSpan.FromSeconds(5), out Focus atTribute));
        Assert.Equal(typeof(NearMapUnitCalculator), atTribute.Calculator);
        Assert.Equal(hero, atTribute.Target);
        Assert.True(atTribute.Points > 9.25f);
        Assert.True(atTribute.Points < 9.50f);
    }

    [Fact]
    public void HeroOutsideRange_IsNotFocused()
    {
        Player hero = Hero("Valla", 50, 0);
        ReplayUnit tribute = Moving("RavenLordTribute", 5, 0, 0);
        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(Replay(hero, tribute), Settings());

        Assert.DoesNotContain(
            focus.Values,
            value => value.Calculator == typeof(NearMapUnitCalculator)
        );
    }

    [Fact]
    public void CloserHero_BeatsTheFartherHero()
    {
        Player close = Hero("Valla", 0, 0);
        Player far = Hero("Illidan", 8, 0);
        ReplayUnit dragon = Moving("VehicleDragon", 5, 0, 0);
        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Replay(close, dragon, far),
            Settings()
        );

        Assert.Equal(close, focus[TimeSpan.FromSeconds(5)].Target);
        Assert.True(focus[TimeSpan.FromSeconds(5)].Points > 9.25f);
    }

    [Fact]
    public void StationaryPointBorn_IsUsedWhenTheUnitHasNoPath()
    {
        Player hero = Hero("Valla", 0, 0);
        var altar = new ReplayUnit
        {
            Name = "ScoringAltar",
            TimeSpanBorn = TimeSpan.FromSeconds(1),
            PointBorn = new Point { X = 3, Y = 4 },
        };
        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(Replay(hero, altar), Settings());

        Assert.Equal(typeof(NearMapUnitCalculator), focus[TimeSpan.FromSeconds(5)].Calculator);
    }

    [Fact]
    public void DeadUnit_AndWarnings_AreIgnored()
    {
        Player hero = Hero("Valla", 0, 0);
        ReplayUnit dead = Moving("RavenLordTribute", 5, 0, 0);
        dead.TimeSpanDied = TimeSpan.FromSeconds(4);
        ReplayUnit warning = Moving("RavenLordTributeWarning", 5, 0, 0);
        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Replay(hero, dead, warning),
            Settings()
        );

        Assert.DoesNotContain(
            focus.Values,
            value => value.Calculator == typeof(NearMapUnitCalculator)
        );
    }

    [Fact]
    public void Objective_OutweighsACampAndAGlobe()
    {
        Player hero = Hero("Valla", 0, 0);
        ReplayUnit tribute = Moving("RavenLordTribute", 5, 1, 0);
        ReplayUnit camp = Moving("MercDefenderMeleeKnight", 5, 1, 0);
        ReplayUnit globe = Moving("RegenGlobe", 5, 1, 0);
        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Replay(hero, globe, camp, tribute),
            Settings()
        );

        Focus chosen = focus[TimeSpan.FromSeconds(5)];
        Assert.Contains("RavenLordTribute", chosen.Description);
        Assert.True(chosen.Points > 9f);
    }

    [Fact]
    public void AppSettings_ListsTheReportedUnitNames()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement focus = document.RootElement.GetProperty("FocusUnits");
        string[] objectives = Strings(focus, "ObjectiveContains");
        string[] camps = Strings(focus, "CampContains");
        string[] pickups = Strings(focus, "PickupContains");

        Assert.Contains("XelNagaWatchTower", objectives);
        Assert.Contains("VehicleDragon", objectives);
        Assert.Contains("VolskayaVehicle", objectives);
        Assert.Contains("GardenTerror", objectives);
        Assert.Contains("BossDuelBoss", objectives);
        Assert.Contains("TempleGuardianBoss", objectives);
        Assert.Contains("SlimeBoss", objectives);
        Assert.Contains("MercPunisher", objectives);
        Assert.Contains("MercDefenderMeleeKnight", camps);
        Assert.Contains("RegenGlobe", pickups);
        Assert.Contains("HealingPotion", pickups);
        Assert.Equal(
            2.2,
            document.RootElement.GetProperty("Weights").GetProperty("Pickup").GetDouble()
        );
        Assert.True(
            document
                .RootElement.GetProperty("Calculators")
                .GetProperty("Enabled")
                .GetProperty("NearMapUnitCalculator")
                .GetBoolean()
        );
    }

    private static string[] Strings(JsonElement parent, string name)
    {
        return parent.GetProperty(name).EnumerateArray().Select(item => item.GetString()).ToArray();
    }

    private static IReadOnlyDictionary<TimeSpan, Focus> Analyze(Replay replay, AppSettings settings)
    {
        var analyzer = new ReplayAnalyzer(
            NullLogger<ReplayAnalyzer>.Instance,
            settings,
            null,
            new[] { new NearMapUnitCalculator(settings) },
            null
        );
        return analyzer.GetPlayers(replay);
    }

    private static AppSettings Settings()
    {
        return new AppSettings
        {
            Weights = new WeightSettings
            {
                MapObjective = 9.25f,
                CampClear = 2.5f,
                Pickup = 2.2f,
                PlayerDeath = 9.5f,
            },
            Spectate = new SpectateSettings { MaxDistanceToObjective = 10 },
            FocusUnits = new FocusUnitSettings
            {
                ObjectiveContains = new[] { "RavenLordTribute", "ScoringAltar", "VehicleDragon" },
                CampContains = new[] { "MercDefenderMeleeKnight" },
                PickupContains = new[] { "RegenGlobe" },
            },
        };
    }

    private static Replay Replay(Player hero, params ReplayUnit[] units)
    {
        return new Replay
        {
            Frames = 128,
            Players = new[] { hero },
            Units = units.ToList(),
        };
    }

    private static Replay Replay(Player first, ReplayUnit unit, Player second)
    {
        return new Replay
        {
            Frames = 128,
            Players = new[] { first, second },
            Units = new List<ReplayUnit> { unit },
        };
    }

    private static Player Hero(string character, int x, int y)
    {
        var player = new Player
        {
            Name = character,
            Character = character,
            HeroUnits = new List<ReplayUnit>(),
        };
        player.HeroUnits.Add(
            new ReplayUnit
            {
                Name = "Hero" + character,
                TimeSpanBorn = TimeSpan.Zero,
                PlayerControlledBy = player,
                Positions = new List<Position>
                {
                    new Position
                    {
                        TimeSpan = TimeSpan.FromSeconds(5),
                        Point = new Point { X = x, Y = y },
                    },
                },
            }
        );
        return player;
    }

    private static ReplayUnit Moving(string name, int second, int x, int y)
    {
        return new ReplayUnit
        {
            Name = name,
            TimeSpanBorn = TimeSpan.FromSeconds(1),
            Positions = new List<Position>
            {
                new Position
                {
                    TimeSpan = TimeSpan.FromSeconds(second),
                    Point = new Point { X = x, Y = y },
                },
            },
        };
    }
}
