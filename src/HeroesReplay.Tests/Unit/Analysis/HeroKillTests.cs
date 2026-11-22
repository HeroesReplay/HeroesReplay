using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Clips;
using Xunit;
using ReplayUnit = Heroes.ReplayParser.Unit;

namespace HeroesReplay.Tests.Unit.Analysis;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroKillTests
{
    [Fact]
    public void Read_NoPlayerDeathEvents_IsNull()
    {
        Replay replay = Match(out _, out _);

        Assert.Null(HeroKills.Read(replay));
    }

    [Fact]
    public void Read_DeathWithADeadHeroUnit_NamesTheBlow()
    {
        Replay replay = Match(out Player killer, out ReplayUnit killerUnit);
        Die(replay, 1, 100, killer, killerUnit, killers: new[] { 0, 5 });

        HeroKill kill = Assert.Single(HeroKills.Read(replay));

        Assert.Equal(1, kill.VictimIndex);
        Assert.Equal(0, kill.BlowIndex);
        Assert.Equal(new[] { 0, 5 }, kill.KillerIndexes);
        Assert.NotNull(kill.Body);
    }

    [Fact]
    public void FromReplay_ChenSpiritsAndDvaMech_AreNotKills()
    {
        // Replay 65719247: "Artanis quadra kill" was Chen and his three spirits.
        Replay replay = Match(out Player killer, out ReplayUnit killerUnit);
        Player chen = replay.Players[1];
        chen.HeroUnits[0].Name = "HeroChen";
        foreach (string spirit in new[] { "HeroChenStorm", "HeroChenEarth", "HeroChenFire" })
        {
            chen.HeroUnits.Add(Dead(chen, spirit, 101, killer, killerUnit));
        }

        Player dva = replay.Players[2];
        dva.HeroUnits[0].Name = "HeroDVaPilot";
        dva.HeroUnits.Add(Dead(dva, "HeroDVaMech", 99, killer, killerUnit));
        Die(replay, 1, 103, killer, killerUnit);
        Die(replay, 2, 104, killer, killerUnit);

        IReadOnlyList<TeamKillDeath> deaths = TeamKillDeaths.FromReplay(replay);

        Assert.Equal(2, deaths.Count);
        Assert.Empty(TeamKillClips.Select(deaths));
    }

    [Fact]
    public void FromReplay_ThreeVikings_AreOneKill()
    {
        // Replay 65760223: "Alarak triple kill" was three Lost Vikings in one second.
        Replay replay = Match(out Player killer, out ReplayUnit killerUnit);
        Player vikings = replay.Players[1];
        vikings.HeroUnits.Clear();
        foreach (string viking in new[] { "HeroOlaf", "HeroBaleog", "HeroErik" })
        {
            vikings.HeroUnits.Add(Dead(vikings, viking, 200, killer, killerUnit));
            replay.TrackerEvents.Add(PlayerDeath(200, 1, new[] { 0 }));
        }

        IReadOnlyList<TeamKillDeath> deaths = TeamKillDeaths.FromReplay(replay);

        Assert.Single(deaths);
    }

    [Fact]
    public void FromReplay_SameVikingsAfterTheWindow_AreANewKill()
    {
        Replay replay = Match(out Player killer, out ReplayUnit killerUnit);
        Player vikings = replay.Players[1];
        vikings.HeroUnits.Clear();
        vikings.HeroUnits.Add(Dead(vikings, "HeroOlaf", 200, killer, killerUnit));
        replay.TrackerEvents.Add(PlayerDeath(200, 1, new[] { 0 }));
        vikings.HeroUnits.Add(Dead(vikings, "HeroOlaf", 260, killer, killerUnit));
        replay.TrackerEvents.Add(PlayerDeath(260, 1, new[] { 0 }));

        Assert.Equal(2, TeamKillDeaths.FromReplay(replay).Count);
    }

    [Fact]
    public void Read_LeoricDiesWithNoUnit_BlowIsKnownOnlyForASingleEnemy()
    {
        Replay replay = Match(out _, out _);
        replay.Players[1].HeroUnits[0].Name = "HeroLeoric";
        replay.TrackerEvents.Add(PlayerDeath(300, 1, new[] { 0 }));
        replay.TrackerEvents.Add(PlayerDeath(400, 1, new[] { 0, 5 }));

        IReadOnlyList<HeroKill> kills = HeroKills.Read(replay);

        Assert.Equal(2, kills.Count);
        Assert.All(kills, kill => Assert.Null(kill.Body));
        Assert.Equal(0, kills[0].BlowIndex);
        Assert.Equal(-1, kills[1].BlowIndex);
        Assert.Equal(TimeSpan.FromSeconds(300), kills[0].Time);
        TeamKillDeath death = Assert.Single(TeamKillDeaths.FromReplay(replay));
        Assert.Equal(300, death.Second);
    }

    [Fact]
    public void Read_VehicleBlowIsThePlayers_SummonBlowIsNot()
    {
        Replay replay = Match(out Player killer, out _);
        var dragon = new ReplayUnit { Name = "VehicleDragon" };
        var misha = new ReplayUnit { Name = "RexxarMisha" };
        Die(replay, 1, 100, killer, dragon);
        Die(replay, 2, 101, killer, misha);
        Die(replay, 3, 102, killer, null);

        IReadOnlyList<HeroKill> kills = HeroKills.Read(replay);

        Assert.Equal(new[] { 0, -1, 0 }, kills.Select(kill => kill.BlowIndex));
    }

    [Fact]
    public void Read_SuicideAndTeammate_AreNotBlows()
    {
        Replay replay = Match(out _, out _);
        Player victim = replay.Players[1];
        Die(replay, 1, 100, victim, victim.HeroUnits[0]);
        Die(replay, 2, 101, replay.Players[3], replay.Players[3].HeroUnits[0]);

        Assert.All(HeroKills.Read(replay), kill => Assert.Equal(-1, kill.BlowIndex));
    }

    /// <summary>Player 0 (Li-Ming) and 5 on team 0, players 1 to 4 on team 1, every hero alive.</summary>
    private static Replay Match(out Player killer, out ReplayUnit killerUnit)
    {
        killerUnit = new ReplayUnit { Name = "HeroWizard" };
        killer = Hero("Li-Ming", 0, killerUnit);
        var players = new List<Player> { killer };
        foreach (string name in new[] { "Artanis", "Butcher", "Chromie", "Diablo" })
        {
            players.Add(Hero(name, 1, new ReplayUnit { Name = "Hero" + name }));
        }

        players.Add(Hero("Tassadar", 0, new ReplayUnit { Name = "HeroTassadar" }));
        return new Replay { Players = players.ToArray(), TrackerEvents = new List<TrackerEvent>() };
    }

    /// <summary>The player's first hero unit dies, with its <c>PlayerDeath</c> event.</summary>
    private static void Die(
        Replay replay,
        int victim,
        int second,
        Player killer,
        ReplayUnit killerUnit,
        int[] killers = null
    )
    {
        ReplayUnit unit = replay.Players[victim].HeroUnits[0];
        unit.TimeSpanDied = TimeSpan.FromSeconds(second);
        unit.PlayerKilledBy = killer;
        unit.UnitKilledBy = killerUnit;
        unit.PointDied = new Point { X = 50, Y = 60 };
        replay.TrackerEvents.Add(
            PlayerDeath(second, victim, killers ?? new[] { Array.IndexOf(replay.Players, killer) })
        );
    }

    private static ReplayUnit Dead(
        Player owner,
        string name,
        int second,
        Player killer,
        ReplayUnit killerUnit
    )
    {
        return new ReplayUnit
        {
            Name = name,
            PlayerControlledBy = owner,
            TimeSpanDied = TimeSpan.FromSeconds(second),
            PlayerKilledBy = killer,
            UnitKilledBy = killerUnit,
            PointDied = new Point { X = 50, Y = 60 },
        };
    }

    private static Player Hero(string name, int team, params ReplayUnit[] units)
    {
        var player = new Player
        {
            Name = name,
            Character = name,
            Team = team,
            HeroUnits = new List<ReplayUnit>(units),
        };
        foreach (ReplayUnit unit in units)
        {
            unit.PlayerControlledBy = player;
        }

        return player;
    }

    /// <summary>
    /// <c>{"PlayerDeath", , [{{"PlayerID"}, 2}, {{"KillingPlayer"}, 1}], [{{"PositionX"}, 50}, {{"PositionY"}, 60}]}</c>
    /// with one-based player ids, as the game writes it.
    /// </summary>
    internal static TrackerEvent PlayerDeath(int second, int victim, int[] killers)
    {
        var ids = new List<TrackerEventStructure> { Pair("PlayerID", victim + 1) };
        ids.AddRange(killers.Select(killer => Pair("KillingPlayer", killer + 1)));
        return new TrackerEvent
        {
            TimeSpan = TimeSpan.FromSeconds(second),
            TrackerEventType = ReplayTrackerEvents.TrackerEventType.StatGameEvent,
            Data = new TrackerEventStructure
            {
                dictionary = new Dictionary<int, TrackerEventStructure>
                {
                    [0] = new TrackerEventStructure
                    {
                        blob = Encoding.UTF8.GetBytes("PlayerDeath"),
                    },
                    [2] = new TrackerEventStructure
                    {
                        optionalData = new TrackerEventStructure { array = ids.ToArray() },
                    },
                    [3] = new TrackerEventStructure
                    {
                        optionalData = new TrackerEventStructure
                        {
                            array = new[] { Pair("PositionX", 50), Pair("PositionY", 60) },
                        },
                    },
                },
            },
        };
    }

    private static TrackerEventStructure Pair(string key, int value)
    {
        return new TrackerEventStructure
        {
            dictionary = new Dictionary<int, TrackerEventStructure>
            {
                [0] = new TrackerEventStructure
                {
                    dictionary = new Dictionary<int, TrackerEventStructure>
                    {
                        [0] = new TrackerEventStructure { blob = Encoding.UTF8.GetBytes(key) },
                    },
                },
                [1] = new TrackerEventStructure { vInt = value },
            },
        };
    }
}
