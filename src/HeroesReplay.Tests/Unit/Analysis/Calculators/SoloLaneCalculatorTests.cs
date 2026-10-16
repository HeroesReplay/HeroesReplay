using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Analysis.Calculators;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Spectating;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ReplayUnit = Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.Analysis.Calculators;

/// <summary>
/// A solo laner alone with one enemy hero for <c>Spectate:SoloLaneDuelTime</c> is a duel worth
/// <c>Weights:SoloLaneDuel</c>, as much as a 4v4. A kill or a death and its lead-in, a map objective
/// and a bigger fight still swing the camera (#437).
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class SoloLaneCalculatorTests
{
    // The solo laners duel in the top lane; the other eight fight a 4v4 in the middle.
    private readonly Player blueSolo = Hero("Sonya", 0, 0, 200);
    private readonly Player redSolo = Hero("Dehaka", 1, 10, 200);
    private readonly Player[] blue = Enumerable
        .Range(0, 4)
        .Select(i => Hero($"Blue{i}", 0, 100 + 2 * i, 0))
        .ToArray();
    private readonly Player[] red = Enumerable
        .Range(0, 4)
        .Select(i => Hero($"Red{i}", 1, 110 + 2 * i, 0))
        .ToArray();

    [Fact]
    public void Duel_HoldsTheCamera_AgainstA4v4Elsewhere()
    {
        Replay replay = Game(40);

        IReadOnlyDictionary<TimeSpan, Focus> withoutDuel = Analyze(
            replay,
            Settings(duel: 0),
            new NearEnemyCalculator(Settings()),
            Duels(Settings(duel: 0))
        );
        Assert.Contains("4v4 fight", withoutDuel[Second(20)].Description);

        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            replay,
            new NearEnemyCalculator(Settings()),
            Duels(Settings())
        );
        Assert.All(
            Enumerable.Range(0, 40),
            s =>
            {
                Assert.Equal(typeof(SoloLaneCalculator), focus[Second(s)].Calculator);
                Assert.Equal(blueSolo, focus[Second(s)].Target);
            }
        );
        Assert.Contains("Sonya duels Dehaka in a solo lane", focus[Second(0)].Description);
    }

    [Fact]
    public void MapObjective_StillTakesTheCamera()
    {
        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(40),
            new NearEnemyCalculator(Settings()),
            Duels(Settings()),
            new Offers(typeof(MapObjectiveCalculator), (20, 29, red[0], 9.25f))
        );

        Assert.Equal(blueSolo, focus[Second(19)].Target);
        Assert.Equal(red[0], focus[Second(20)].Target);
        Assert.Equal(red[0], focus[Second(29)].Target);
    }

    [Fact]
    public void LeadIn_BeatsTheDuel_FromItsFirstSecond()
    {
        // A death that matters (60 s to respawn) has a 30 s lead-in, from 8 at 30 to 10 at 50.
        // A 4v4 (9.11) would keep the camera until about 42; the duel (9.15) never does.
        Kill(blue[0], red[0], at: 60, respawn: 120);

        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(120),
            new NearEnemyCalculator(Settings()),
            new KillCalculator(Settings()),
            Duels(Settings())
        );

        Assert.Equal(blueSolo, focus[Second(29)].Target);
        Assert.All(Enumerable.Range(30, 30), s => Assert.Equal(blue[0], focus[Second(s)].Target));
    }

    [Fact]
    public void DuelWhileTheHeroIsDead_DoesNotLengthenTheLeadIn()
    {
        // 6 s to respawn: a 12 s lead-in (48 to 59). A duel worth 9.15 while the hero is dead is
        // not the impact (9) that would make it 30 s.
        Kill(blue[0], red[0], at: 60, respawn: 66);

        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(120),
            new NearEnemyCalculator(Settings()),
            new KillCalculator(Settings()),
            Duels(Settings())
        );

        Assert.Equal(blueSolo, focus[Second(45)].Target);
        Assert.Equal(blue[0], focus[Second(48)].Target);
    }

    [Fact]
    public void Duel_DoesNotCutAFreshFightShot()
    {
        // The red solo laner walks into the duel at 10. A 2v2 (8.43) took the camera at 8; the
        // duel is a fight too, not an outcome, so it waits for the shot to reach 4 s.
        Player late = Hero("Dehaka", 1, s => s < 10 ? (200, 200) : (10, 200));
        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(40, blueSolo, late, blue[0], red[0]),
            Duels(Settings(), blueSolo, late),
            new Offers(typeof(RoamingCalculator), (0, 7, blue[0], 5f)),
            new Offers(typeof(NearEnemyCalculator), (8, 39, red[0], 8.43f))
        );

        Assert.Equal(red[0], focus[Second(10)].Target);
        Assert.Equal(red[0], focus[Second(11)].Target);
        Assert.Equal(blueSolo, focus[Second(12)].Target);
    }

    [Fact]
    public void ThirdHeroNearADuelist_IsNoDuel()
    {
        Player ally = Hero("Valla", 0, 0, 215);

        Assert.Empty(DuelFoci(Game(40, blueSolo, redSolo, ally), Duels(Settings())));
    }

    [Fact]
    public void Brush_ShorterThanTheDuelTime_IsNoDuel()
    {
        Player passing = Hero("Dehaka", 1, s => s is >= 5 and <= 15 ? (10, 200) : (200, 200));
        Assert.Empty(DuelFoci(Game(40, blueSolo, passing), Duels(Settings(), blueSolo, passing)));

        Player staying = Hero("Dehaka", 1, s => s is >= 5 and <= 16 ? (10, 200) : (200, 200));
        Assert.Equal(
            Enumerable.Range(5, 12),
            DuelFoci(Game(40, blueSolo, staying), Duels(Settings(), blueSolo, staying)).Keys
        );
    }

    [Fact]
    public void OnlyASoloLanersOneOnOne_IsADuel_AndFollowsTheSoloLaner()
    {
        // Two other heroes fight 1v1 in the bottom lane; the red hero facing the blue solo laner
        // is no solo laner itself.
        Player bottomBlue = Hero("Raynor", 0, 0, -200);
        Player bottomRed = Hero("Zeratul", 1, 10, -200);

        IReadOnlyDictionary<int, Focus> duels = DuelFoci(
            Game(40, redSolo, blueSolo, bottomBlue, bottomRed),
            Duels(Settings(), blueSolo)
        );

        Assert.Equal(40, duels.Count);
        Assert.All(duels.Values, focus => Assert.Equal(blueSolo, focus.Target));
    }

    [Fact]
    public void Duel_FollowsTheDuelistWhoDies()
    {
        Kill(redSolo, blueSolo, at: 25, respawn: 60);

        IReadOnlyDictionary<int, Focus> duels = DuelFoci(Game(40), Duels(Settings()));

        Assert.Equal(Enumerable.Range(0, 25), duels.Keys);
        Assert.All(duels.Values, focus => Assert.Equal(redSolo, focus.Target));
    }

    [Fact]
    public void SoloLaners_OfAReplayWithoutLanes_AreNone()
    {
        Assert.Empty(SoloLaneCalculator.SoloLaners(Game(40), null, null, NullLogger.Instance));
        Assert.Empty(
            DuelFoci(
                Game(40),
                new SoloLaneCalculator(
                    Settings(),
                    gameData: null,
                    NullLogger<SoloLaneCalculator>.Instance
                )
            )
        );
    }

    private static TimeSpan Second(int second) => TimeSpan.FromSeconds(second);

    private SoloLaneCalculator Duels(AppSettings settings, params Player[] solo) =>
        new(settings, _ => solo.Length > 0 ? solo : new[] { blueSolo, redSolo });

    private Replay Game(int seconds) =>
        Game(seconds, blue.Concat(red).Prepend(redSolo).Prepend(blueSolo).ToArray());

    private Replay Game(int seconds, params Player[] players) =>
        new()
        {
            Frames = seconds * 16,
            Players = players,
            Units = players.SelectMany(player => player.HeroUnits).ToList(),
        };

    private static IReadOnlyDictionary<TimeSpan, Focus> Analyze(
        Replay replay,
        params IFocusCalculator[] calculators
    ) => Analyze(replay, Settings(), calculators);

    private static IReadOnlyDictionary<TimeSpan, Focus> Analyze(
        Replay replay,
        AppSettings settings,
        params IFocusCalculator[] calculators
    ) =>
        new ReplayAnalyzer(
            NullLogger<ReplayAnalyzer>.Instance,
            settings,
            null,
            calculators,
            null
        ).GetPlayers(replay);

    /// <summary>The seconds the calculator alone offers a duel, and the focus it offers.</summary>
    private static IReadOnlyDictionary<int, Focus> DuelFoci(
        Replay replay,
        SoloLaneCalculator calculator
    )
    {
        ReplayTimeline timeline = ReplayTimeline.Create(replay);
        calculator.Contribute(timeline);
        return timeline
            .ToDictionary()
            .Where(pair => pair.Value.Calculator == typeof(SoloLaneCalculator))
            .ToDictionary(pair => (int)pair.Key.TotalSeconds, pair => pair.Value);
    }

    private static AppSettings Settings(float duel = 9.15f) =>
        new()
        {
            Weights = new WeightSettings
            {
                NearEnemyHero = 8.0f,
                NearEnemyHeroOffset = 0.09f,
                NearEnemyHeroDistanceDivisor = 10000f,
                TeamfightPerHero = 0.17f,
                TeamfightMax = 9.4f,
                SoloLaneDuel = duel,
                PlayerKill = 10,
                PlayerDeath = 9.5f,
                DeathLeadInFloor = 8,
                DeathImpact = 9,
                FocusDwellMargin = 0.5f,
                FocusDwellOutcome = 9,
            },
            Spectate = new SpectateSettings
            {
                MaxDistanceToEnemy = 20,
                MaxDistanceToEnemyKill = 15,
                PastDeathContextTime = Second(10),
                MaxPastDeathContextTime = Second(30),
                DeathContextFullRespawn = Second(60),
                PresentDeathContextTime = Second(3),
                KillStreakWindow = Second(12),
                FocusShareDistance = 5,
                FocusDwellTime = Second(4),
                SoloLaneDuelTime = Second(12),
            },
        };

    /// <summary>A hero standing still.</summary>
    private static Player Hero(string character, int team, int x, int y) =>
        Hero(character, team, _ => (x, y));

    private static Player Hero(string character, int team, Func<int, (int X, int Y)> at)
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
                Positions = Enumerable
                    .Range(0, 300)
                    .Select(second => new Position
                    {
                        TimeSpan = Second(second),
                        Point = new Point { X = at(second).X, Y = at(second).Y },
                    })
                    .ToList(),
            }
        );
        return player;
    }

    /// <summary>The victim's hero dies at <paramref name="at"/> and its next life starts at <paramref name="respawn"/>.</summary>
    private static void Kill(Player victim, Player killer, int at, int respawn)
    {
        ReplayUnit life = victim.HeroUnits[^1];
        life.TimeSpanDied = Second(at);
        life.PlayerKilledBy = killer;
        life.PointDied = life.Positions[at].Point;
        victim.HeroUnits.Add(
            new ReplayUnit
            {
                Name = life.Name,
                Team = life.Team,
                TimeSpanBorn = Second(respawn),
                PlayerControlledBy = victim,
            }
        );
    }

    /// <summary>Offers a player's hero from one second to another at fixed points, as the calculator given.</summary>
    private sealed class Offers(
        Type calculator,
        params (int From, int To, Player Player, float Points)[] offers
    ) : IFocusCalculator
    {
        public void Contribute(ReplayTimeline timeline)
        {
            foreach ((int start, int end, Player player, float points) in offers)
            {
                timeline.OfferRange(
                    Second(start),
                    Second(end),
                    calculator,
                    player.HeroUnits[0],
                    player,
                    points,
                    $"{player.Character} {calculator.Name}"
                );
            }
        }
    }
}
