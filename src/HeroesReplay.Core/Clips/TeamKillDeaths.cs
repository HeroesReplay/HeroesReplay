using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Clips;

public static class TeamKillDeaths
{
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
                        killerKey
                    )
                );
            }
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
