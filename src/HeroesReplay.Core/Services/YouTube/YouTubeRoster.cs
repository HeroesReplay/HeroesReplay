using System.Collections.Generic;
using Heroes.ReplayParser;

namespace HeroesReplay.Core.Services.YouTube;

public static class YouTubeRoster
{
    public static string[] Lines(Replay replay)
    {
        if (replay?.Players == null)
        {
            return [];
        }

        string blue = Line("Blue", replay.Players, team: 0);
        string red = Line("Red", replay.Players, team: 1);
        if (blue == null && red == null)
        {
            return [];
        }

        if (blue == null)
        {
            return [red];
        }

        if (red == null)
        {
            return [blue];
        }

        return [blue, red];
    }

    private static string Line(string teamName, IEnumerable<Player> players, int team)
    {
        var parts = new List<string>();
        foreach (Player player in players)
        {
            if (player == null || player.Team != team)
            {
                continue;
            }

            string part = Describe(player);
            if (!string.IsNullOrWhiteSpace(part))
            {
                parts.Add(part);
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        return teamName + ": " + string.Join(", ", parts);
    }

    private static string Describe(Player player)
    {
        string hero = Hero(player);
        if (player.PlayerType == PlayerType.Computer)
        {
            return hero == null ? "AI" : hero + " (AI)";
        }

        string account = Account(player);
        if (hero != null && account != null)
        {
            return hero + " (" + account + ")";
        }

        return hero ?? account;
    }

    private static string Hero(Player player)
    {
        if (!string.IsNullOrWhiteSpace(player.Character))
        {
            return player.Character.Trim();
        }

        if (!string.IsNullOrWhiteSpace(player.HeroAttributeId))
        {
            return player.HeroAttributeId.Trim();
        }

        return null;
    }

    private static string Account(Player player)
    {
        string name = player.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (name.Contains('#'))
        {
            return name;
        }

        if (player.BattleTag > 0)
        {
            return name + "#" + player.BattleTag;
        }

        return name;
    }
}
