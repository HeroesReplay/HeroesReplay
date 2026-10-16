using System;
using System.Globalization;
using System.Text.Json;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>A player's Storm League record on one hero, from Heroes Profile's <c>players/heroes</c>.</summary>
public sealed record PlayerHeroStats(
    int Wins,
    int Losses,
    int Games,
    double? Kda,
    double? HeroMmr,
    string RankTier
)
{
    /// <summary>Wins over games, 0 to 1; null without games.</summary>
    public double? WinRate => Games <= 0 ? null : (double)Math.Clamp(Wins, 0, Games) / Games;
}

public enum PlayerHeroLookupStatus
{
    /// <summary>Heroes Profile answered with the player's games on the hero.</summary>
    Known,

    /// <summary>The player has no Storm League game on the hero.</summary>
    NoGames,

    /// <summary>The player's profile is private (403 <c>player_unavailable</c>).</summary>
    Private,

    /// <summary>No player has that BattleTag in that region (404 <c>player_not_found</c>).</summary>
    NotFound,

    /// <summary>Not answered: a 422, a failed job, a network error, or a bad BattleTag.</summary>
    Unavailable,

    /// <summary>The key cannot make the call (401, or a 403 that is not about the player).</summary>
    Refused,

    /// <summary>The weekly call budget is spent.</summary>
    OverBudget,
}

/// <summary>One lookup's answer; <see cref="Stats"/> only when <see cref="Status"/> is Known.</summary>
public sealed record PlayerHeroLookup(
    PlayerHeroLookupStatus Status,
    PlayerHeroStats Stats = null,
    string Reason = null,
    bool Cached = false
)
{
    public bool IsKnown => Status == PlayerHeroLookupStatus.Known && Stats != null;
}

/// <summary>
/// Reads a <c>players/heroes?…&amp;hero=X&amp;game_type=sl</c> answer: an array with a row per
/// hero (<c>wins</c>, <c>losses</c>, <c>games_played</c>, <c>kda</c>, <c>sl_mmr_data</c>,
/// <c>sl_rank_tier</c>, and <c>hero.attribute_id</c>). Measured on 2026-10-16: one row for the
/// hero asked about, Storm League games only.
/// </summary>
public static class PlayerHeroStatsJson
{
    /// <summary>The hero's row (by attribute id, else name, else the only row); null without one.</summary>
    public static PlayerHeroStats Parse(string body, string attributeId, string heroName)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            JsonElement? match = null;
            int rows = 0;
            JsonElement only = default;
            foreach (JsonElement row in root.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                rows++;
                only = row;
                if (IsHero(row, attributeId, heroName))
                {
                    match = row;
                    break;
                }
            }

            JsonElement chosen = match ?? (rows == 1 ? only : default);
            if (chosen.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            int wins = Int(chosen, "wins") ?? 0;
            int losses = Int(chosen, "losses") ?? 0;
            int games = Int(chosen, "games_played") ?? wins + losses;
            return games <= 0
                ? null
                : new PlayerHeroStats(
                    wins,
                    losses,
                    games,
                    Number(chosen, "kda"),
                    Mmr(chosen),
                    Text(chosen, "sl_rank_tier")
                );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsHero(JsonElement row, string attributeId, string heroName)
    {
        if (
            row.TryGetProperty("hero", out JsonElement hero)
            && hero.ValueKind == JsonValueKind.Object
        )
        {
            if (
                !string.IsNullOrWhiteSpace(attributeId)
                && string.Equals(
                    Text(hero, "attribute_id"),
                    attributeId,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            if (
                !string.IsNullOrWhiteSpace(heroName)
                && string.Equals(Text(hero, "name"), heroName, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return !string.IsNullOrWhiteSpace(heroName)
            && string.Equals(Text(row, "name"), heroName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>sl_mmr_data</c> is a number in <c>players/heroes</c> and an object with <c>mmr</c> elsewhere.</summary>
    private static double? Mmr(JsonElement row)
    {
        if (!row.TryGetProperty("sl_mmr_data", out JsonElement data))
        {
            return null;
        }

        return data.ValueKind switch
        {
            JsonValueKind.Number => data.GetDouble(),
            JsonValueKind.Object => Number(data, "mmr"),
            _ => null,
        };
    }

    private static int? Int(JsonElement element, string name)
    {
        double? number = Number(element, name);
        return number == null ? null : (int)Math.Round(number.Value);
    }

    private static double? Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
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

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
