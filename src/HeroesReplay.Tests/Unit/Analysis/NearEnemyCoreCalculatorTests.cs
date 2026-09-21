using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
public class NearEnemyCoreCalculatorTests
{
    [Fact]
    public void EarlySiege_UsesTheNormalCoreWeight()
    {
        Focus focus = FocusAt(10, includeTribute: false);
        Assert.Equal(typeof(NearEnemyCoreCalculator), focus.Calculator);
        Assert.Contains("near enemy core", focus.Description);
        Assert.Equal(8.5f, focus.Points);
    }

    [Fact]
    public void PushThatKillsTheCore_OutweighsAMapObjective()
    {
        Focus focus = FocusAt(70, includeTribute: true);
        Assert.Equal(typeof(NearEnemyCoreCalculator), focus.Calculator);
        Assert.Contains("ending on enemy core", focus.Description);
        Assert.Equal(9.6f, focus.Points);
    }

    [Fact]
    public void HeroFarFromTheCore_IsNotFocused()
    {
        Player hero = Hero("Valla", 80, 0);
        ReplayUnit core = Core();
        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(ReplayOf(hero, core), false);

        Assert.DoesNotContain(
            focus.Values,
            value => value.Calculator == typeof(NearEnemyCoreCalculator)
        );
    }

    [Fact]
    public void SampleReplay_FocusesNamedMapUnitsByDistance()
    {
        const string path =
            @"C:\heroesreplay\replays-for-analysis\33811142_906ff904-21e9-2d4a-f3c5-90f7b0fa2c58.StormReplay";
        if (!File.Exists(path))
        {
            return;
        }

        (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(
            File.ReadAllBytes(path),
            new ParseOptions
            {
                IgnoreErrors = true,
                AllowPTR = true,
                ShouldParseEvents = true,
                ShouldParseUnits = true,
                ShouldParseMouseEvents = false,
                ShouldParseMessageEvents = false,
                ShouldParseStatistics = false,
                ShouldParseDetailedBattleLobby = false,
            }
        );
        Assert.NotNull(replay);
        Assert.Equal(DataParser.ReplayParseResult.UnexpectedResult, result);

        var settings = Settings();
        settings.FocusUnits.ObjectiveContains = new[]
        {
            "RavenLordTribute",
            "XelNagaWatchTower",
            "VehicleDragon",
        };
        settings.FocusUnits.PickupContains = new[] { "RegenGlobe" };
        var analyzer = new ReplayAnalyzer(
            NullLogger<ReplayAnalyzer>.Instance,
            settings,
            null,
            new IFocusCalculator[]
            {
                new NearMapUnitCalculator(settings),
                new NearEnemyCoreCalculator(settings, null),
            },
            null
        );

        IReadOnlyDictionary<TimeSpan, Focus> focus = analyzer.GetPlayers(replay);
        Assert.Contains(
            focus.Values,
            value =>
                value.Calculator == typeof(NearMapUnitCalculator)
                && (
                    value.Description.Contains("RavenLordTribute")
                    || value.Description.Contains("XelNagaWatchTower")
                    || value.Description.Contains("RegenGlobe")
                )
        );
    }

    private static Focus FocusAt(int second, bool includeTribute)
    {
        Player hero = Hero("Valla", 0, 0);
        ReplayUnit core = Core();
        ReplayUnit[] units = includeTribute
            ? new[] { core, Moving("RavenLordTribute", second, 0, 0) }
            : new[] { core };
        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(ReplayOf(hero, units), includeTribute);
        Assert.True(focus.TryGetValue(TimeSpan.FromSeconds(second), out Focus chosen));
        return chosen;
    }

    private static IReadOnlyDictionary<TimeSpan, Focus> Analyze(Replay replay, bool includeTribute)
    {
        AppSettings settings = Settings();
        var calculators = new List<IFocusCalculator>
        {
            new NearMapUnitCalculator(settings),
            new NearEnemyCoreCalculator(settings, null),
        };
        if (!includeTribute)
        {
            calculators.RemoveAt(0);
        }

        var analyzer = new ReplayAnalyzer(
            NullLogger<ReplayAnalyzer>.Instance,
            settings,
            null,
            calculators,
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
                NearEnemyCore = 8.5f,
                EndingCore = 9.6f,
                MapObjective = 9.25f,
                CampClear = 2.5f,
                Pickup = 2.2f,
            },
            Spectate = new SpectateSettings
            {
                MaxDistanceToCore = 15,
                MaxDistanceToObjective = 10,
                EndingCoreWindow = TimeSpan.FromSeconds(45),
            },
            FocusUnits = new FocusUnitSettings
            {
                CoreContains = new[] { "KingsCore" },
                ObjectiveContains = new[] { "RavenLordTribute" },
            },
        };
    }

    private static Replay ReplayOf(Player hero, params ReplayUnit[] units)
    {
        return new Replay
        {
            Frames = 110 * 16,
            Players = new[] { hero },
            Units = units.ToList(),
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
                Team = 0,
                Positions = new List<Position>
                {
                    new Position
                    {
                        TimeSpan = TimeSpan.FromSeconds(1),
                        Point = new Point { X = x, Y = y },
                    },
                },
            }
        );
        return player;
    }

    private static ReplayUnit Core()
    {
        return new ReplayUnit
        {
            Name = "KingsCore",
            TimeSpanBorn = TimeSpan.Zero,
            TimeSpanDied = TimeSpan.FromSeconds(100),
            Team = 1,
            PointBorn = new Point { X = 0, Y = 0 },
        };
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
