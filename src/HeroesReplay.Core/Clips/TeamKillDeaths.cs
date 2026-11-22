using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Clips;

/// <summary>
/// The hero killing blows of a replay, for its kill clips. They come from the game's own hero
/// deaths (<see cref="HeroKills"/>), so a spirit of Chen, D.Va's mech or a copy by Abathur is not
/// a kill, and a kill on Leoric, Tyrael or Uther counts when the replay names who landed it. A
/// replay with no such events falls back to the deaths of hero units.
/// </summary>
public static class TeamKillDeaths
{
    /// <summary>
    /// A further body of the same partial victim (<see cref="HeroKill.Partial"/>) killed by the
    /// same player within this long is the same kill. It is the clips' chain window.
    /// </summary>
    private static readonly TimeSpan PartialDeathWindow = TimeSpan.FromSeconds(12);

    public static IReadOnlyList<TeamKillDeath> FromReplay(
        Replay replay,
        IReadOnlyList<Hero> heroes = null
    )
    {
        if (replay?.Players == null)
        {
            return Array.Empty<TeamKillDeath>();
        }

        var indexOf = new Dictionary<Player, int>();
        var names = new Dictionary<Player, string>();
        for (int index = 0; index < replay.Players.Length; index++)
        {
            Player player = replay.Players[index];
            if (player != null && !indexOf.ContainsKey(player))
            {
                indexOf[player] = index;
                names[player] = HeroName(replay, player, heroes);
            }
        }

        IReadOnlyList<HeroKill> kills = HeroKills.Read(replay);
        if (kills != null)
        {
            return FromKills(replay.Players, kills, names);
        }

        var deaths = new List<TeamKillDeath>();
        foreach (Player player in replay.Players)
        {
            if (player?.HeroUnits == null)
            {
                continue;
            }

            string victim = names[player];
            if (victim == null)
            {
                continue;
            }

            foreach (Unit unit in player.HeroUnits)
            {
                if (!IsEnemyHeroUnitBlow(player, unit, out Player killer))
                {
                    continue;
                }

                if (
                    !indexOf.TryGetValue(killer, out int killerKey)
                    || names[killer] is not string killerName
                )
                {
                    continue;
                }

                deaths.Add(
                    new TeamKillDeath(
                        unit.TimeSpanDied.Value.FloorSeconds(),
                        killerName,
                        victim,
                        killerKey,
                        indexOf[player]
                    )
                );
            }
        }

        return deaths;
    }

    /// <summary>
    /// The killing blows among the game's own hero deaths (<see cref="HeroKills"/>). A body of The
    /// Lost Vikings or of Murky that the same player kills again within
    /// <see cref="PartialDeathWindow"/> is the same kill, so three Vikings are not a triple kill.
    /// </summary>
    private static IReadOnlyList<TeamKillDeath> FromKills(
        Player[] players,
        IReadOnlyList<HeroKill> kills,
        Dictionary<Player, string> names
    )
    {
        var deaths = new List<TeamKillDeath>();
        var lastPartial = new Dictionary<(int Killer, int Victim), TimeSpan>();
        foreach (HeroKill kill in kills)
        {
            if (kill.BlowIndex < 0)
            {
                continue;
            }

            Player killer = players[kill.BlowIndex];
            Player victim = players[kill.VictimIndex];
            if (
                !names.TryGetValue(killer, out string killerName)
                || killerName == null
                || !names.TryGetValue(victim, out string victimName)
                || victimName == null
            )
            {
                continue;
            }

            if (kill.Partial)
            {
                (int, int) pair = (kill.BlowIndex, kill.VictimIndex);
                bool repeat =
                    lastPartial.TryGetValue(pair, out TimeSpan last)
                    && kill.Time - last <= PartialDeathWindow;
                lastPartial[pair] = kill.Time;
                if (repeat)
                {
                    continue;
                }
            }

            deaths.Add(
                new TeamKillDeath(
                    kill.Time.FloorSeconds(),
                    killerName,
                    victimName,
                    kill.BlowIndex,
                    kill.VictimIndex
                )
            );
        }

        return deaths;
    }

    /// <summary>
    /// The dead unit is one player's hero unit, and the killing unit is a different
    /// player's hero unit on the other team. Summons, structures, and suicides are not blows.
    /// </summary>
    private static bool IsEnemyHeroUnitBlow(Player victim, Unit unit, out Player killer)
    {
        killer = null;
        if (unit?.TimeSpanDied == null)
        {
            return false;
        }

        killer = unit.PlayerKilledBy;
        Unit blow = unit.UnitKilledBy;
        if (
            killer == null
            || ReferenceEquals(killer, victim)
            || killer.Team == victim.Team
            || blow == null
            || killer.HeroUnits == null
            || !killer.HeroUnits.Contains(blow)
        )
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// The hero the player played (<see cref="PlayedHero"/>), else the player's name. The lobby
    /// hero is not the played hero in ARAM (#348).
    /// </summary>
    private static string HeroName(Replay replay, Player player, IReadOnlyList<Hero> heroes)
    {
        string hero = PlayedHero.Name(heroes, replay, player);
        if (hero != null)
        {
            return hero;
        }

        return string.IsNullOrWhiteSpace(player.Name) ? null : player.Name.Trim();
    }
}
