using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube.Metadata;

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
        for (int index = 0; index < replay.Players.Length; index++)
        {
            Player player = replay.Players[index];
            if (player != null && !indexOf.ContainsKey(player))
            {
                indexOf[player] = index;
            }
        }

        var deaths = new List<TeamKillDeath>();
        foreach (Player player in replay.Players)
        {
            if (player?.HeroUnits == null)
            {
                continue;
            }

            string victim = HeroName(player, heroes);
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

                string killerName = HeroName(killer, heroes);
                if (killerName == null || !indexOf.TryGetValue(killer, out int killerKey))
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

    private static string HeroName(Player player, IReadOnlyList<Hero> heroes)
    {
        if (player == null)
        {
            return null;
        }

        if (heroes != null && heroes.Count > 0)
        {
            Hero match =
                HeroDraft.Find(heroes, player.HeroAttributeId)
                ?? HeroDraft.Find(heroes, player.HeroId)
                ?? HeroDraft.Find(heroes, player.Character);
            if (!string.IsNullOrWhiteSpace(match?.Name))
            {
                return match.Name.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(player.Character))
        {
            return player.Character.Trim();
        }

        return string.IsNullOrWhiteSpace(player.Name) ? null : player.Name.Trim();
    }
}
