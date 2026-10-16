using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace HeroesReplay.Core.HeroFocus.Targeted;

/// <summary>One game from a player's match history.</summary>
public sealed record PlayerMatch(
    int ReplayId,
    DateTime PlayedAtUtc,
    bool Won,
    int Kills,
    int Deaths,
    int Assists
)
{
    /// <summary>(Kills + assists) per death, a death-free game counting as one death.</summary>
    public double Kda => (Kills + Assists) / (double)Math.Max(1, Deaths);
}

/// <summary>
/// Heroes Profile's <c>/players/matches</c> (#433): a player's games, each with its replay id and
/// score line. A cold query answers 202 with a job, collected like any other.
/// </summary>
public static class PlayerMatches
{
    public const string Path = "players/matches";

    /// <summary>The games in the answer's <c>data</c>; one without a replay id or date is skipped.</summary>
    public static IReadOnlyList<PlayerMatch> Parse(string body)
    {
        var matches = new List<PlayerMatch>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return matches;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            JsonElement data =
                root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("data", out JsonElement rows)
                    ? rows
                    : root;
            if (data.ValueKind != JsonValueKind.Array)
            {
                return matches;
            }

            foreach (JsonElement row in data.EnumerateArray())
            {
                int replayId = HeroLeaderboard.Number(row, "replayID") is double id ? (int)id : 0;
                string date = HeroLeaderboard.Text(row, "game_date");
                if (
                    replayId <= 0
                    || !DateTime.TryParse(
                        date,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                        out DateTime played
                    )
                )
                {
                    continue;
                }

                matches.Add(
                    new PlayerMatch(
                        replayId,
                        played,
                        HeroLeaderboard.Number(row, "winner") == 1,
                        Count(row, "kills"),
                        Count(row, "deaths"),
                        Count(row, "assists")
                    )
                );
            }
        }
        catch (JsonException)
        {
            return Array.Empty<PlayerMatch>();
        }

        return matches;
    }

    /// <summary>The games worth fetching first: won before lost, then the better score, then the newest.</summary>
    public static IReadOnlyList<PlayerMatch> Best(IEnumerable<PlayerMatch> matches) =>
        (matches ?? Enumerable.Empty<PlayerMatch>())
            .OrderByDescending(match => match.Won)
            .ThenByDescending(match => match.Kda)
            .ThenByDescending(match => match.PlayedAtUtc)
            .ThenByDescending(match => match.ReplayId)
            .ToList();

    private static int Count(JsonElement row, string name) =>
        HeroLeaderboard.Number(row, name) is double value ? (int)value : 0;
}
