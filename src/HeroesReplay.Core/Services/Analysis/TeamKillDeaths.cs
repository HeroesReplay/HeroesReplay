using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Core.Extensions;

namespace HeroesReplay.Core.Services.Analysis;

public static class TeamKillDeaths
{
    public static IReadOnlyList<TeamKillDeath> FromReplay(Replay replay)
    {
        if (replay?.Players == null)
        {
            return Array.Empty<TeamKillDeath>();
        }

        var deaths = new List<TeamKillDeath>();
        foreach (Player player in replay.Players)
        {
            if (player?.HeroUnits == null)
            {
                continue;
            }

            foreach (Unit unit in player.HeroUnits)
            {
                if (unit?.TimeSpanDied == null || unit.PlayerKilledBy == null)
                {
                    continue;
                }

                string killer = HeroName(unit.PlayerKilledBy);
                string victim = HeroName(player);
                if (killer == null || victim == null)
                {
                    continue;
                }

                deaths.Add(
                    new TeamKillDeath(unit.TimeSpanDied.Value.FloorSeconds(), killer, victim)
                );
            }
        }

        return deaths;
    }

    private static string HeroName(Player player)
    {
        if (!string.IsNullOrWhiteSpace(player?.Character))
        {
            return player.Character;
        }

        return string.IsNullOrWhiteSpace(player?.Name) ? null : player.Name;
    }
}
