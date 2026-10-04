using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Analysis.Calculators;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ReplayUnit = Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.Analysis.Calculators;

/// <summary>
/// #234: the camera follows what is happening, not where a hero stands. Each test is one rule.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ActivityFocusTests
{
    private static readonly TimeSpan At = TimeSpan.FromSeconds(5);

    [Fact]
    public void Structure_ScoresOnlyWhileAnEnemyHeroContestsIt()
    {
        ReplayUnit altar = Standing("ScoringAltar", 0, 0);
        Player alone = Hero("Thrall", 0, Still(1, 0));

        Assert.False(
            Analyze(Game(new[] { alone }, altar), new NearMapUnitCalculator(Settings()))
                .ContainsKey(At)
        );

        Player blue = Hero("Thrall", 0, Still(1, 0));
        Player red = Hero("Leoric", 1, Still(4, 0));
        Focus contested = Analyze(
            Game(new[] { blue, red }, altar),
            new NearMapUnitCalculator(Settings())
        )[At];
        Assert.Contains("contested ScoringAltar", contested.Description);
        Assert.InRange(contested.Points, 6.0f, 6.5f);
    }

    [Fact]
    public void LiveObjective_ScoresActivity_ACampScoresNothing()
    {
        Player hero = Hero("Valla", 0, Still(1, 0));
        Focus tribute = Analyze(
            Game(new[] { hero }, Standing("RavenLordTribute", 0, 0)),
            new NearMapUnitCalculator(Settings())
        )[At];
        Assert.InRange(tribute.Points, 6.0f, 6.5f);

        Assert.False(
            Analyze(
                    Game(new[] { hero }, Standing("MercDefenderMeleeKnight", 0, 0)),
                    new NearMapUnitCalculator(Settings())
                )
                .ContainsKey(At)
        );
    }

    [Fact]
    public void IdleAbathurBody_IsSkipped_UnlessAnEnemyIsOnHim()
    {
        ReplayUnit tribute = Standing("RavenLordTribute", 0, 0);
        Player abathur = Hero("Abathur", 0, Still(1, 0));
        Assert.False(
            Analyze(Game(new[] { abathur }, tribute), new NearMapUnitCalculator(Settings()))
                .ContainsKey(At)
        );

        Player hunted = Hero("Abathur", 0, Still(1, 0));
        Player hunter = Hero("Zeratul", 1, Still(6, 0));
        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(new[] { hunted, hunter }, tribute),
            new NearMapUnitCalculator(Settings())
        );
        Assert.Equal(hunted, focus[At].Target);
    }

    [Fact]
    public void Payload_PathFollowsItsEscortAndEndsWhereItWasDelivered()
    {
        // Spawned at 0,0 at 1 s, pushed by Blue towards 0,40, delivered at 0,40 at 21 s.
        var escort = Enumerable.Range(1, 21).Select(s => (s, 0, (s - 1) * 2)).ToArray();
        Player blue = Hero("Rehgar", 0, escort);
        var payload = new ReplayUnit
        {
            Name = "Payload_Neutral",
            TimeSpanBorn = TimeSpan.FromSeconds(1),
            TimeSpanDied = TimeSpan.FromSeconds(30),
            PointBorn = new Point { X = 0, Y = 0 },
            Positions = new List<Position>
            {
                new()
                {
                    TimeSpan = TimeSpan.FromSeconds(21),
                    Point = new Point { X = 0, Y = 40 },
                },
            },
            OwnerChangeEvents = new List<OwnerChangeEvent>
            {
                new() { TimeSpanOwnerChanged = TimeSpan.FromSeconds(1), Team = 0 },
            },
        };
        ReplayTimeline timeline = ReplayTimeline.Create(Game(new[] { blue }, 40, payload));

        Dictionary<int, Point> path = EscortedPath.Track(payload, timeline);

        Assert.Equal(new Point { X = 0, Y = 0 }.ToString(), path[1].ToString());
        Assert.InRange(path[11].Y, 15, 25);
        Assert.Equal(40, path[21].Y);
        Assert.Equal(40, path[28].Y);
    }

    [Fact]
    public void CaptureBeacon_ScoresOnlyInTheRunUpToACapture()
    {
        var beacon = new ReplayUnit
        {
            Name = "WatchTowerCaptureBeacon",
            TimeSpanBorn = TimeSpan.Zero,
            PointBorn = new Point { X = 0, Y = 0 },
            OwnerChangeEvents = new List<OwnerChangeEvent>
            {
                new() { TimeSpanOwnerChanged = TimeSpan.FromSeconds(8), Team = 0 },
            },
        };
        Player hero = Hero("Valla", 0, Enumerable.Range(0, 12).Select(s => (s, 1, 0)).ToArray());
        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(new[] { hero }, 12, beacon),
            new NearCaptureBeaconCalculator(Settings())
        );

        Assert.False(focus.ContainsKey(TimeSpan.FromSeconds(1)));
        Assert.Equal(
            typeof(NearCaptureBeaconCalculator),
            focus[TimeSpan.FromSeconds(5)].Calculator
        );
        Assert.Equal(
            typeof(NearCaptureBeaconCalculator),
            focus[TimeSpan.FromSeconds(8)].Calculator
        );
    }

    [Fact]
    public void BiggerFight_WinsOverASkirmish_AndTheCameraTakesAnEngagedHero()
    {
        // A 1v1 at 100,100, and a 3v3 at 0,0 with a Blue backliner linked only through allies.
        Player duelBlue = Hero("Illidan", 0, Still(100, 100));
        Player duelRed = Hero("Genji", 1, Still(103, 100));
        Player blueA = Hero("Muradin", 0, Still(0, 0));
        Player blueB = Hero("Jaina", 0, Still(-15, 0));
        Player blueBack = Hero("Lucio", 0, Still(-30, 0));
        Player redA = Hero("Diablo", 1, Still(5, 0));
        Player redB = Hero("Valla", 1, Still(10, 0));
        Player[] players = { duelBlue, duelRed, blueA, blueB, blueBack, redA, redB };

        Focus focus = Analyze(Game(players), new NearEnemyCalculator(Settings()))[At];

        Assert.Contains("fight", focus.Description);
        Assert.NotEqual(blueBack, focus.Target);
        Assert.Contains(focus.Target, new[] { blueA, blueB, redA, redB });
        Assert.True(focus.Points > 8.5f);
        Assert.True(focus.Points <= 9.4f);
    }

    [Fact]
    public void AbathurSymbioteEnding_IsNotADeath()
    {
        Player abathur = Hero("Abathur", 0, Still(0, 0));
        var symbiote = new ReplayUnit
        {
            Name = "AbathurSymbiote",
            TimeSpanBorn = TimeSpan.FromSeconds(1),
            TimeSpanDied = At,
            PlayerControlledBy = abathur,
        };
        var game = new GroupedGameData(new[] { "AbathurSymbiote", "HeroAbathur" });

        Assert.Empty(
            Analyze(Game(new[] { abathur }, symbiote), new DeathCalculator(Settings(), game))
        );

        ReplayUnit body = abathur.HeroUnits[0];
        body.TimeSpanDied = At;
        Focus death = Analyze(Game(new[] { abathur }), new DeathCalculator(Settings(), game))[At];
        Assert.Equal(abathur, death.Target);
    }

    private static IReadOnlyDictionary<TimeSpan, Focus> Analyze(
        Replay replay,
        IFocusCalculator calculator
    )
    {
        var analyzer = new ReplayAnalyzer(
            NullLogger<ReplayAnalyzer>.Instance,
            Settings(),
            null,
            new[] { calculator },
            null
        );
        return analyzer.GetPlayers(replay);
    }

    private static AppSettings Settings() =>
        new()
        {
            Weights = new WeightSettings
            {
                MapObjective = 9.25f,
                ObjectiveActivity = 6.0f,
                CampClear = 2.5f,
                Pickup = 2.2f,
                CaptureBeacon = 3.0f,
                NearEnemyHero = 8.0f,
                NearEnemyHeroOffset = 0.09f,
                NearEnemyHeroDistanceDivisor = 10000f,
                TeamfightPerHero = 0.17f,
                TeamfightMax = 9.4f,
                PlayerDeath = 9.5f,
            },
            Spectate = new SpectateSettings
            {
                MaxDistanceToObjective = 10,
                MaxDistanceToEnemy = 20,
                MaxDistanceToOwnerChange = 5,
                RemoteBodyHeroes = new[] { "Abathur" },
            },
            FocusUnits = new FocusUnitSettings
            {
                ObjectiveContains = new[] { "RavenLordTribute", "ScoringAltar", "Payload_Neutral" },
                StructureContains = new[] { "ScoringAltar" },
                EscortContains = new[] { "Payload_Neutral" },
                CampContains = new[] { "MercDefenderMeleeKnight" },
                PickupContains = new[] { "RegenGlobe" },
            },
            HeroesToolChest = new HeroesToolChestSettings
            {
                CaptureContains = new[] { "CaptureBeacon" },
            },
        };

    private static Replay Game(Player[] players, params ReplayUnit[] units) =>
        Game(players, 8, units);

    private static Replay Game(Player[] players, int seconds, params ReplayUnit[] units) =>
        new()
        {
            Frames = seconds * 16,
            Players = players,
            Units = units.Concat(players.SelectMany(player => player.HeroUnits)).ToList(),
        };

    private static (int Second, int X, int Y)[] Still(int x, int y) =>
        Enumerable.Range(0, 8).Select(second => (second, x, y)).ToArray();

    private static Player Hero(string character, int team, (int Second, int X, int Y)[] path)
    {
        var player = new Player
        {
            Name = character,
            Character = character,
            Team = team,
            HeroUnits = new List<ReplayUnit>(),
        };
        player.HeroUnits.Add(
            new ReplayUnit
            {
                Name = "Hero" + character,
                Team = team,
                TimeSpanBorn = TimeSpan.Zero,
                PlayerControlledBy = player,
                Positions = path.Select(step => new Position
                    {
                        TimeSpan = TimeSpan.FromSeconds(step.Second),
                        Point = new Point { X = step.X, Y = step.Y },
                    })
                    .ToList(),
            }
        );
        return player;
    }

    private static ReplayUnit Standing(string name, int x, int y) =>
        new()
        {
            Name = name,
            TimeSpanBorn = TimeSpan.FromSeconds(1),
            PointBorn = new Point { X = x, Y = y },
        };

    /// <summary>Every listed unit is in the Hero group, as heroes-data puts Abathur's Symbiote.</summary>
    private sealed class GroupedGameData(string[] heroGroup) : IGameData
    {
        public IReadOnlyDictionary<string, ReplayUnit.UnitGroup> UnitGroups { get; } =
            new Dictionary<string, ReplayUnit.UnitGroup>();

        public IReadOnlyList<Hero> Heroes { get; } = Array.Empty<Hero>();

        public IReadOnlyCollection<string> CoreUnits { get; } = Array.Empty<string>();

        public IReadOnlyCollection<string> BossUnits { get; } = Array.Empty<string>();

        public IReadOnlyCollection<string> VehicleUnits { get; } = Array.Empty<string>();

        public IReadOnlyList<Map> Maps { get; } = Array.Empty<Map>();

        public ReplayUnit.UnitGroup GetUnitGroup(string unitName) =>
            heroGroup.Contains(unitName) ? ReplayUnit.UnitGroup.Hero : ReplayUnit.UnitGroup.Unknown;

        public Task LoadDataAsync() => Task.CompletedTask;
    }
}
