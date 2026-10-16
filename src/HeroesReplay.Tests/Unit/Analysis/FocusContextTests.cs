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
/// The story before a death (#431) and a steady camera between heroes in one view (#432).
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class FocusContextTests
{
    [Fact]
    public void LeadIn_FollowsTheHeroWhoDies_ForItsShareOfTheRespawnTimer()
    {
        // Killed at 40 s, back at 55 s: a 15 s timer is a quarter of the full 60 s, so the lead-in
        // is 10 s plus a quarter of the 20 s more it can grow.
        Player victim = Hero("Jaina", 0, x: 0);
        Player killer = Hero("Valla", 1, x: 10);
        Kill(victim, killer, at: 40, respawn: 55);

        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(120, victim, killer),
            new KillCalculator(Settings())
        );

        Assert.False(focus.ContainsKey(Second(24)));
        for (int second = 25; second < 40; second++)
        {
            Assert.Equal(victim, focus[Second(second)].Target);
            Assert.Equal(typeof(KillCalculator), focus[Second(second)].Calculator);
        }

        Assert.Contains("1s before: Valla kills Jaina", focus[Second(39)].Description);
        Assert.Equal(killer, focus[Second(40)].Target);
    }

    [Fact]
    public void LeadIn_IsLongest_ForAFullRespawnTimer()
    {
        Player victim = Hero("Jaina", 0, x: 0);
        Player killer = Hero("Valla", 1, x: 10);
        Kill(victim, killer, at: 100, respawn: 160);

        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(200, victim, killer),
            new KillCalculator(Settings())
        );

        Assert.False(focus.ContainsKey(Second(69)));
        Assert.Equal(victim, focus[Second(70)].Target);
    }

    [Fact]
    public void LeadIn_IsLongest_WhenAnObjectiveFallsWhileTheHeroIsDead()
    {
        Player victim = Hero("Jaina", 0, x: 0);
        Player killer = Hero("Valla", 1, x: 10);
        Player capper = Hero("Muradin", 1, x: 80);
        Kill(victim, killer, at: 40, respawn: 55);

        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(120, victim, killer, capper),
            new KillCalculator(Settings()),
            new Scripted((50, 50, capper, 9.25f))
        );

        Assert.False(focus.ContainsKey(Second(9)));
        Assert.Equal(victim, focus[Second(10)].Target);
    }

    [Fact]
    public void LeadIn_Fades_SoAFightElsewhereKeepsTheCameraEarlyButNotLate()
    {
        Player victim = Hero("Jaina", 0, x: 0);
        Player killer = Hero("Valla", 1, x: 10);
        Player fighter = Hero("Muradin", 1, x: 80);
        Kill(victim, killer, at: 40, respawn: 100);

        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(120, victim, killer, fighter),
            new KillCalculator(Settings()),
            new Scripted((12, 12, fighter, 9.0f), (35, 35, fighter, 9.0f))
        );

        Assert.Equal(fighter, focus[Second(12)].Target);
        Assert.Equal(victim, focus[Second(13)].Target);
        Assert.Equal(victim, focus[Second(35)].Target);
    }

    [Fact]
    public void LeadIn_StartsNoEarlierThanTheHerosLife()
    {
        Player victim = Hero("Jaina", 0, x: 0, born: 35);
        Player killer = Hero("Valla", 1, x: 10);
        Kill(victim, killer, at: 40, respawn: 100);

        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(120, victim, killer),
            new KillCalculator(Settings())
        );

        Assert.False(focus.ContainsKey(Second(35)));
        Assert.Equal(victim, focus[Second(36)].Target);
    }

    [Fact]
    public void LeadIn_ComesBeforeEveryKill_NotOnlyAKillersFirst()
    {
        Player first = Hero("Jaina", 0, x: 0);
        Player second = Hero("Uther", 0, x: 40);
        Player killer = Hero("Valla", 1, x: 10);
        Kill(first, killer, at: 40, respawn: 55);
        Kill(second, killer, at: 100, respawn: 115);

        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(160, first, second, killer),
            new KillCalculator(Settings())
        );

        Assert.Equal(first, focus[Second(35)].Target);
        Assert.Equal(second, focus[Second(95)].Target);
    }

    [Fact]
    public void NearbyHero_KeepsTheCamera_AFarOneTakesIt()
    {
        Player shown = Hero("Jaina", 0, x: 0);
        Player near = Hero("Valla", 0, x: 3);
        IReadOnlyDictionary<TimeSpan, Focus> kept = Analyze(
            Game(30, shown, near),
            Scripted.Swap(shown, near)
        );

        Assert.Equal(shown, kept[Second(15)].Target);
        Assert.Contains("camera kept on Jaina, 3.0 away", kept[Second(15)].Description);

        Player far = Hero("Valla", 0, x: 12);
        IReadOnlyDictionary<TimeSpan, Focus> swapped = Analyze(
            Game(30, shown, far),
            Scripted.Swap(shown, far)
        );

        Assert.Equal(far, swapped[Second(10)].Target);
    }

    [Fact]
    public void KeptCamera_Moves_WhenTheWinnerWalksAwayOrTheShownHeroDies()
    {
        Player shown = Hero("Jaina", 0, x: 0);
        Player walker = Hero("Valla", 0, second => second < 15 ? 3 : 20);
        IReadOnlyDictionary<TimeSpan, Focus> walked = Analyze(
            Game(30, shown, walker),
            Scripted.Swap(shown, walker)
        );

        Assert.Equal(shown, walked[Second(14)].Target);
        Assert.Equal(walker, walked[Second(15)].Target);

        Player dying = Hero("Jaina", 0, x: 0);
        dying.HeroUnits[0].TimeSpanDied = Second(12);
        Player near = Hero("Valla", 0, x: 3);
        IReadOnlyDictionary<TimeSpan, Focus> died = Analyze(
            Game(30, dying, near),
            Scripted.Swap(dying, near)
        );

        Assert.Equal(dying, died[Second(11)].Target);
        Assert.Equal(near, died[Second(12)].Target);
    }

    [Fact]
    public void ShareDistanceZero_SwapsAsBefore()
    {
        Player shown = Hero("Jaina", 0, x: 0);
        Player near = Hero("Valla", 0, x: 3);

        IReadOnlyDictionary<TimeSpan, Focus> focus = Analyze(
            Game(30, shown, near),
            Settings(share: 0),
            Scripted.Swap(shown, near)
        );

        Assert.Equal(near, focus[Second(10)].Target);
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

    private static AppSettings Settings(double share = 5) =>
        new()
        {
            Weights = new WeightSettings
            {
                PlayerKill = 10,
                PlayerDeath = 9.5f,
                DeathLeadInFloor = 8,
                DeathImpact = 9,
            },
            Spectate = new SpectateSettings
            {
                MaxDistanceToEnemyKill = 15,
                PastDeathContextTime = Second(10),
                MaxPastDeathContextTime = Second(30),
                DeathContextFullRespawn = Second(60),
                PresentDeathContextTime = Second(3),
                KillStreakWindow = Second(12),
                FocusShareDistance = share,
                FocusDwellTime = TimeSpan.Zero,
            },
        };

    private static Replay Game(int seconds, params Player[] players) =>
        new()
        {
            Frames = seconds * 16,
            Players = players,
            Units = players.SelectMany(player => player.HeroUnits).ToList(),
        };

    private static Player Hero(string character, int team, int x, int born = 0) =>
        Hero(character, team, _ => x, born);

    /// <summary>A hero standing on the X axis, at <paramref name="x"/> of each second.</summary>
    private static Player Hero(string character, int team, Func<int, int> x, int born = 0)
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
                TimeSpanBorn = Second(born),
                PlayerControlledBy = player,
                Positions = Enumerable
                    .Range(born, 300 - born)
                    .Select(second => new Position
                    {
                        TimeSpan = Second(second),
                        Point = new Point { X = x(second), Y = 0 },
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

    /// <summary>Offers a player's hero at fixed seconds and points.</summary>
    private sealed class Scripted(params (int From, int To, Player Player, float Points)[] offers)
        : IFocusCalculator
    {
        /// <summary><paramref name="from"/> wins 0-9 s, <paramref name="to"/> wins 10-19 s.</summary>
        public static Scripted Swap(Player from, Player to) =>
            new((0, 9, from, 5), (10, 19, to, 8));

        public void Contribute(ReplayTimeline timeline)
        {
            foreach ((int start, int end, Player player, float points) in offers)
            {
                timeline.OfferRange(
                    Second(start),
                    Second(end),
                    GetType(),
                    player.HeroUnits[0],
                    player,
                    points,
                    $"{player.Character} scripted"
                );
            }
        }
    }
}
