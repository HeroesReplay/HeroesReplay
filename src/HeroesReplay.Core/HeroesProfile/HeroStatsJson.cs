using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>A hero from <c>GET /heroes</c>: the id the stats rows use, its name, and its <c>attribute_id</c>.</summary>
public sealed record HeroStatsHeroRef(int Id, string Name, string AttributeId);

/// <summary>The ally and enemy rows of one <c>GET /heroes/matchups</c> answer.</summary>
public sealed record HeroMatchups(List<HeroPairStats> Enemies, List<HeroPairStats> Allies);

/// <summary>
/// Reads the Heroes Profile global statistics bodies. Numbers may arrive as strings. Every rate is
/// computed from <c>wins</c> and <c>games_played</c>: a matchups <c>enemy[].win_rate</c> is the
/// queried hero's loss rate against that enemy ("Lost against a team with X"), not its win rate.
/// </summary>
public static class HeroStatsJson
{
    private static readonly JsonSerializerOptions FileOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
    };

    /// <summary>Every hero of <c>GET /heroes</c> that has an id, a name, and an <c>attribute_id</c>.</summary>
    public static IReadOnlyList<HeroStatsHeroRef> ReadHeroes(string body)
    {
        var heroes = new List<HeroStatsHeroRef>();
        using JsonDocument document = Parse(body);
        if (
            document == null
            || !document.RootElement.TryGetProperty("heroes", out JsonElement list)
            || list.ValueKind != JsonValueKind.Array
        )
        {
            return heroes;
        }

        foreach (JsonElement hero in list.EnumerateArray())
        {
            int? id = Int(hero, "id");
            string name = Text(hero, "name");
            string attribute = Text(hero, "attribute_id");
            if (id is int value && name != null && attribute != null)
            {
                heroes.Add(new HeroStatsHeroRef(value, name, attribute));
            }
        }

        return heroes;
    }

    /// <summary>
    /// The major patches (<c>2.57</c>) of <c>GET /patches</c> that global statistics accept,
    /// newest first.
    /// </summary>
    public static IReadOnlyList<string> ReadMajorPatches(string body)
    {
        var versions = new List<Version>();
        using JsonDocument document = Parse(body);
        if (
            document == null
            || !document.RootElement.TryGetProperty("patches", out JsonElement list)
            || list.ValueKind != JsonValueKind.Array
        )
        {
            return Array.Empty<string>();
        }

        foreach (JsonElement patch in list.EnumerateArray())
        {
            if (
                patch.TryGetProperty("valid_globals", out JsonElement valid)
                && valid.ValueKind == JsonValueKind.False
            )
            {
                continue;
            }

            string major = HeroStatsPatch.Major(Text(patch, "game_version"));
            if (major != null && Version.TryParse(major, out Version parsed))
            {
                versions.Add(parsed);
            }
        }

        return versions
            .Distinct()
            .OrderByDescending(version => version)
            .Select(version => version.ToString(2))
            .ToArray();
    }

    /// <summary>
    /// <c>GET /heroes/stats?group_by_map=true</c>: one object per map name, each with a
    /// <c>data</c> row per hero. Returns each hero id's rows, games above zero only.
    /// </summary>
    public static Dictionary<int, List<HeroMapStats>> ReadMapStats(string body)
    {
        var byHero = new Dictionary<int, List<HeroMapStats>>();
        using JsonDocument document = Parse(body);
        if (document == null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return byHero;
        }

        foreach (JsonProperty map in document.RootElement.EnumerateObject())
        {
            if (
                string.IsNullOrWhiteSpace(map.Name)
                || map.Value.ValueKind != JsonValueKind.Object
                || !map.Value.TryGetProperty("data", out JsonElement rows)
                || rows.ValueKind != JsonValueKind.Array
            )
            {
                continue;
            }

            foreach (JsonElement row in rows.EnumerateArray())
            {
                int? heroId = Int(row, "hero_id");
                (int wins, int games) = Record(row);
                if (heroId is not int id || games <= 0)
                {
                    continue;
                }

                if (!byHero.TryGetValue(id, out List<HeroMapStats> list))
                {
                    list = new List<HeroMapStats>();
                    byHero[id] = list;
                }

                list.Add(
                    new HeroMapStats
                    {
                        Map = map.Name.Trim(),
                        Wins = wins,
                        Games = games,
                        BanRate = Number(row, "ban_rate") ?? 0,
                    }
                );
            }
        }

        return byHero;
    }

    /// <summary>
    /// <c>GET /heroes/matchups</c>: the queried hero's wins and games against each enemy and with
    /// each ally, keyed by the other hero's <c>attribute_id</c>. <c>win_rate</c> is ignored.
    /// </summary>
    public static HeroMatchups ReadMatchups(string body)
    {
        var enemies = new List<HeroPairStats>();
        var allies = new List<HeroPairStats>();
        using JsonDocument document = Parse(body);
        if (document != null && document.RootElement.ValueKind == JsonValueKind.Object)
        {
            ReadPairs(document.RootElement, "enemy", enemies);
            ReadPairs(document.RootElement, "ally", allies);
        }

        return new HeroMatchups(enemies, allies);
    }

    public static string Write(HeroStatsSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, FileOptions);

    /// <summary>The saved snapshot, or null when the text is not one.</summary>
    public static HeroStatsSnapshot ReadSnapshot(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            HeroStatsSnapshot snapshot = JsonSerializer.Deserialize<HeroStatsSnapshot>(
                text,
                FileOptions
            );
            return snapshot?.Heroes == null ? null : snapshot;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ReadPairs(JsonElement root, string name, List<HeroPairStats> into)
    {
        if (
            !root.TryGetProperty(name, out JsonElement rows)
            || rows.ValueKind != JsonValueKind.Array
        )
        {
            return;
        }

        foreach (JsonElement row in rows.EnumerateArray())
        {
            string attribute =
                row.TryGetProperty("hero", out JsonElement hero)
                && hero.ValueKind == JsonValueKind.Object
                    ? Text(hero, "attribute_id")
                    : null;
            (int wins, int games) = Record(row);
            if (attribute == null || games <= 0)
            {
                continue;
            }

            into.Add(
                new HeroPairStats
                {
                    AttributeId = attribute,
                    Wins = wins,
                    Games = games,
                }
            );
        }
    }

    /// <summary>Wins and games. Games are <c>games_played</c>, or wins plus losses without it.</summary>
    private static (int Wins, int Games) Record(JsonElement row)
    {
        int wins = Math.Max(0, Int(row, "wins") ?? 0);
        int losses = Math.Max(0, Int(row, "losses") ?? 0);
        int games = Int(row, "games_played") ?? wins + losses;
        if (games < wins)
        {
            games = wins;
        }

        return (wins, Math.Max(0, games));
    }

    private static JsonDocument Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Text(JsonElement element, string name)
    {
        if (
            element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out JsonElement value)
        )
        {
            return null;
        }

        string text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static int? Int(JsonElement element, string name)
    {
        double? number = Number(element, name);
        return number is double value && value >= int.MinValue && value <= int.MaxValue
            ? (int)Math.Round(value)
            : null;
    }

    private static double? Number(JsonElement element, string name)
    {
        if (
            element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out JsonElement value)
        )
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
        {
            return number;
        }

        if (
            value.ValueKind == JsonValueKind.String
            && double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed
            )
        )
        {
            return parsed;
        }

        return null;
    }
}
