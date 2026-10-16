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

namespace HeroesReplay.Tests.Unit.Analysis;

/// <summary>
/// A shot lasts at least <c>Spectate:FocusDwellTime</c> unless an outcome (a kill, a death, an
/// objective) worth <c>Weights:FocusDwellMargin</c> more cuts it or the hero on screen dies. A
/// bigger fight never cuts it (#435).
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class FocusDwellTests
{
    private readonly Player first = Hero("Jaina", 0, x: 0);
    private readonly Player second = Hero("Valla", 0, x: 40);
    private readonly Player third = Hero("Muradin", 1, x: 80);

    [Fact]
    public void NewShot_IsHeld_AgainstACloseScoreElsewhere()
    {
        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(30, first, second, third),
            new Scripted((0, 9, first, 5), (10, 19, second, 5.5f), (12, 19, third, 5.6f))
        );

        Assert.Equal(second, focus[Second(10)].Target);
        Assert.Equal(second, focus[Second(13)].Target);
        Assert.Contains(
            "camera held on Valla over Muradin scripted",
            focus[Second(13)].Description
        );
        Assert.Equal(third, focus[Second(14)].Target);
    }

    [Fact]
    public void Objective_CutsTheShot()
    {
        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(30, first, second, third),
            new Scripted((0, 9, first, 5), (10, 19, second, 5.5f), (12, 19, third, 9.25f))
        );

        Assert.Equal(second, focus[Second(11)].Target);
        Assert.Equal(third, focus[Second(12)].Target);
    }

    [Fact]
    public void BiggerFight_DoesNotCutASoloLaneDuel()
    {
        // A 1v1 in a solo lane (8.09) takes the camera at 10; a 4v4 elsewhere (9.1) is a bigger
        // fight with no outcome yet, so it waits for the duel's shot to reach 4 s.
        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(30, first, second, third),
            new Scripted((0, 9, first, 1)),
            new Fight((10, 19, second, 8.09f), (12, 19, third, 9.1f))
        );

        Assert.Equal(second, focus[Second(10)].Target);
        Assert.Equal(second, focus[Second(13)].Target);
        Assert.Equal(third, focus[Second(14)].Target);
    }

    [Fact]
    public void Shot_Ends_WhenTheShownHeroDies()
    {
        // The player keeps another hero alive, like the Lost Vikings or Abathur under an Ultimate
        // Evolution clone: the shot still ends with the hero it shows.
        second.HeroUnits[0].TimeSpanDied = Second(12);
        second.HeroUnits.Add(
            new ReplayUnit
            {
                Name = "HeroValla",
                Team = 0,
                TimeSpanBorn = TimeSpan.Zero,
                PlayerControlledBy = second,
            }
        );

        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(30, first, second, third),
            new Scripted((0, 9, first, 5), (10, 19, second, 5.5f), (12, 19, third, 5.6f))
        );

        Assert.Equal(second, focus[Second(11)].Target);
        Assert.Equal(third, focus[Second(12)].Target);
    }

    [Fact]
    public void ShortGap_KeepsTheCameraOnTheShownHero_ALongOneDoesNot()
    {
        IReadOnlyDictionary<TimeSpan, Focus> bridged = Analyze(
            Game(30, first, second),
            new Scripted((0, 19, first, 5), (10, 11, second, 5.5f))
        );

        Assert.All(Enumerable.Range(0, 20), s => Assert.Equal(first, bridged[Second(s)].Target));

        IReadOnlyDictionary<TimeSpan, Focus> swapped = Analyze(
            Game(30, first, second),
            new Scripted((0, 19, first, 5), (10, 14, second, 5.5f))
        );

        Assert.Equal(second, swapped[Second(10)].Target);
        Assert.Equal(first, swapped[Second(15)].Target);
    }

    [Fact]
    public void KillLeadIn_CutsTheBiggestFight()
    {
        // A 9.4 fight takes the camera at 28, over the lead-in's faded start. The lead-in's
        // full-weight part (10) is a kill's, an outcome above any fight, so it cuts at 30.
        Player victim = Hero("Uther", 0, x: 0);
        Player killer = Hero("Valla", 1, x: 10);
        Player fighter = Hero("Muradin", 1, x: 80);
        Kill(victim, killer, at: 40, respawn: 46);

        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(120, victim, killer, fighter),
            new KillCalculator(Settings()),
            new Fight((28, 39, fighter, 9.4f))
        );

        Assert.Equal(fighter, focus[Second(29)].Target);
        Assert.All(Enumerable.Range(30, 10), s => Assert.Equal(victim, focus[Second(s)].Target));
    }

    [Fact]
    public void LeadIn_StillReachesTheHeroWhoDies_AfterAnObjectiveShot()
    {
        // The push that ends on the core (9.6) takes the camera at 28. The lead-in's full-weight
        // part (10) starts at 30 but is not 0.5 above it, so it waits for the push's shot to reach
        // 4 s and still has the last 8 s before the death.
        Player victim = Hero("Uther", 0, x: 0);
        Player killer = Hero("Valla", 1, x: 10);
        Player pusher = Hero("Muradin", 1, x: 80);
        Kill(victim, killer, at: 40, respawn: 46);

        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(120, victim, killer, pusher),
            new KillCalculator(Settings()),
            new Scripted((28, 39, pusher, 9.6f))
        );

        Assert.Equal(pusher, focus[Second(31)].Target);
        Assert.All(Enumerable.Range(32, 8), s => Assert.Equal(victim, focus[Second(s)].Target));
        Assert.Equal(typeof(KillCalculator), focus[Second(32)].Calculator);
    }

    [Fact]
    public void LeadIn_CutsAFreshShot_WhenItsHeroDiesWithinTheDwell()
    {
        // A streak elsewhere (11.5) takes the camera at 35. When the lead-in wins again at 37 the
        // hero dies in 3 s, inside the dwell, so the camera goes back before the death.
        Player victim = Hero("Uther", 0, x: 0);
        Player killer = Hero("Valla", 1, x: 10);
        Player fighter = Hero("Muradin", 1, x: 80);
        Kill(victim, killer, at: 40, respawn: 100);

        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(120, victim, killer, fighter),
            new KillCalculator(Settings()),
            new Scripted((35, 36, fighter, 11.5f))
        );

        Assert.Equal(victim, focus[Second(34)].Target);
        Assert.Equal(fighter, focus[Second(36)].Target);
        Assert.Equal(victim, focus[Second(37)].Target);
        Assert.Equal(victim, focus[Second(39)].Target);
    }

    [Fact]
    public void DwellOff_SwapsAsBefore()
    {
        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(30, first, second, third),
            Settings(dwell: 0),
            new Scripted((0, 9, first, 5), (10, 19, second, 5.5f), (12, 19, third, 5.6f))
        );

        Assert.Equal(third, focus[Second(12)].Target);
    }

    private static TimeSpan Second(int second) => TimeSpan.FromSeconds(second);

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

    private static AppSettings Settings(int dwell = 4) =>
        new()
        {
            Weights = new WeightSettings
            {
                PlayerKill = 10,
                PlayerDeath = 9.5f,
                DeathLeadInFloor = 8,
                DeathImpact = 9,
                FocusDwellMargin = 0.5f,
                FocusDwellOutcome = 9,
            },
            Spectate = new SpectateSettings
            {
                MaxDistanceToEnemyKill = 15,
                PastDeathContextTime = Second(10),
                MaxPastDeathContextTime = Second(30),
                DeathContextFullRespawn = Second(60),
                PresentDeathContextTime = Second(3),
                KillStreakWindow = Second(12),
                FocusShareDistance = 5,
                FocusDwellTime = Second(dwell),
            },
        };

    private static Replay Game(int seconds, params Player[] players) =>
        new()
        {
            Frames = seconds * 16,
            Players = players,
            Units = players.SelectMany(player => player.HeroUnits).ToList(),
        };

    /// <summary>A hero standing still on the X axis.</summary>
    private static Player Hero(string character, int team, int x)
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
                        Point = new Point { X = x, Y = 0 },
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
        life.PointDied = new Point { X = 0, Y = 0 };
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

    /// <summary>Offers a player's hero from one second to another at fixed points.</summary>
    private class Scripted(params (int From, int To, Player Player, float Points)[] offers)
        : IFocusCalculator
    {
        protected virtual Type Calculator => GetType();

        public void Contribute(ReplayTimeline timeline)
        {
            foreach ((int start, int end, Player player, float points) in offers)
            {
                timeline.OfferRange(
                    Second(start),
                    Second(end),
                    Calculator,
                    player.HeroUnits[0],
                    player,
                    points,
                    $"{player.Character} scripted"
                );
            }
        }
    }

    /// <summary>Offers like a fight between heroes, which is no outcome however big.</summary>
    private sealed class Fight(params (int From, int To, Player Player, float Points)[] offers)
        : Scripted(offers)
    {
        protected override Type Calculator => typeof(NearEnemyCalculator);
    }
}
