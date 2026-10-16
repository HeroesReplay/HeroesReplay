using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace HeroesReplay.Core.HeroFocus.Targeted;

/// <summary>One player's Storm League games on a hero, over every season read.</summary>
public sealed record LeaderboardPlayer(string BattleTag, int Region, int Games, double Wins)
{
    /// <summary>0 to 1.</summary>
    public double WinRate => Games <= 0 ? 0 : Math.Clamp(Wins / Games, 0, 1);
}

/// <summary>
/// Heroes Profile's <c>/leaderboard?type=hero</c> (#433). One season shows few games a player, so
/// the seasons are merged: games add up, and wins are each season's win rate times its games.
/// </summary>
public static class HeroLeaderboard
{
    public const string Path = "leaderboard";

    /// <summary>The rows of one season's board; a row without a BattleTag or region is skipped.</summary>
    public static IReadOnlyList<LeaderboardPlayer> Parse(string body)
    {
        var players = new List<LeaderboardPlayer>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return players;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return players;
            }

            foreach (JsonElement row in document.RootElement.EnumerateArray())
            {
                string battleTag = Text(row, "battletag");
                int region = Number(row, "region_id") is double id ? (int)id : 0;
                int games = Number(row, "games_played") is double played ? (int)played : 0;
                double rate = Number(row, "win_rate") ?? 0;
                if (string.IsNullOrWhiteSpace(battleTag) || region <= 0 || games <= 0)
                {
                    continue;
                }

                players.Add(
                    new LeaderboardPlayer(battleTag.Trim(), region, games, rate / 100 * games)
                );
            }
        }
        catch (JsonException)
        {
            return Array.Empty<LeaderboardPlayer>();
        }

        return players;
    }

    /// <summary>Each player once, most games first, then the higher win rate.</summary>
    public static IReadOnlyList<LeaderboardPlayer> Merge(
        IEnumerable<IReadOnlyList<LeaderboardPlayer>> seasons
    ) =>
        (seasons ?? Enumerable.Empty<IReadOnlyList<LeaderboardPlayer>>())
            .SelectMany(season => season ?? Array.Empty<LeaderboardPlayer>())
            .GroupBy(player => (Tag: player.BattleTag.ToUpperInvariant(), player.Region))
            .Select(group => new LeaderboardPlayer(
                group.First().BattleTag,
                group.Key.Region,
                group.Sum(player => player.Games),
                group.Sum(player => player.Wins)
            ))
            .OrderByDescending(player => player.Games)
            .ThenByDescending(player => player.WinRate)
            .ThenBy(player => player.BattleTag, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The newest season in <c>/patches</c>, or null.</summary>
    public static int? CurrentSeason(string patchesBody)
    {
        if (string.IsNullOrWhiteSpace(patchesBody))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(patchesBody);
            JsonElement patches =
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("patches", out JsonElement list)
                    ? list
                    : document.RootElement;
            if (patches.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            int? newest = null;
            foreach (JsonElement patch in patches.EnumerateArray())
            {
                if (Number(patch, "season") is double season && season > (newest ?? 0))
                {
                    newest = (int)season;
                }
            }

            return newest;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string Text(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object
        && row.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static double? Number(JsonElement row, string name)
    {
        if (
            row.ValueKind != JsonValueKind.Object
            || !row.TryGetProperty(name, out JsonElement value)
        )
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.GetDouble();
        }

        return
            value.ValueKind == JsonValueKind.String
            && double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed
            )
            ? parsed
            : null;
    }
}
