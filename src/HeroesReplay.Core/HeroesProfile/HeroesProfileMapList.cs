using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>One map from Heroes Profile's <c>/maps</c> (#425).</summary>
public sealed record HeroesProfileMap(string Name, string Type, bool RankedRotation, bool Playable);

/// <summary>
/// Heroes Profile's <c>/maps</c> list and when it was fetched (#425). <c>ranked_rotation</c> 1 is
/// a map in the Storm League pool. Anything else (0, "-", missing) is a map outside it. The saved
/// file keeps the API's shape with a <c>fetched_at_utc</c> added, so one reader serves both.
/// </summary>
public sealed class HeroesProfileMapList
{
    public HeroesProfileMapList(DateTimeOffset fetchedAtUtc, IReadOnlyList<HeroesProfileMap> maps)
    {
        FetchedAtUtc = fetchedAtUtc;
        Maps = maps ?? Array.Empty<HeroesProfileMap>();
    }

    public DateTimeOffset FetchedAtUtc { get; }

    public IReadOnlyList<HeroesProfileMap> Maps { get; }

    /// <summary>
    /// The list in <paramref name="json"/>: a <c>/maps</c> answer, or a saved file. A saved file's
    /// <c>fetched_at_utc</c> wins over <paramref name="fetchedAtUtc"/>. Null when the JSON cannot
    /// be read or names no map.
    /// </summary>
    public static HeroesProfileMapList FromJson(string json, DateTimeOffset fetchedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (
                root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("maps", out JsonElement items)
                || items.ValueKind != JsonValueKind.Array
            )
            {
                return null;
            }

            var maps = new List<HeroesProfileMap>();
            foreach (JsonElement item in items.EnumerateArray())
            {
                string name = Text(item, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                maps.Add(
                    new HeroesProfileMap(
                        name.Trim(),
                        Text(item, "type")?.Trim(),
                        Flag(item, "ranked_rotation"),
                        Flag(item, "playable")
                    )
                );
            }

            if (maps.Count == 0)
            {
                return null;
            }

            DateTimeOffset fetched =
                Text(root, "fetched_at_utc") is string saved
                && DateTimeOffset.TryParse(
                    saved,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out DateTimeOffset parsed
                )
                    ? parsed
                    : fetchedAtUtc;
            return new HeroesProfileMapList(fetched, maps);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson()
    {
        var maps = new JsonArray();
        foreach (HeroesProfileMap map in Maps)
        {
            maps.Add(
                (JsonNode)
                    new JsonObject
                    {
                        ["name"] = map.Name,
                        ["type"] = map.Type,
                        ["ranked_rotation"] = map.RankedRotation ? 1 : 0,
                        ["playable"] = map.Playable ? 1 : 0,
                    }
            );
        }

        var root = new JsonObject
        {
            ["fetched_at_utc"] = FetchedAtUtc.ToUniversalTime().ToString("O"),
            ["maps"] = maps,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>The names Heroes Profile marks <c>ranked_rotation</c> 1.</summary>
    public IEnumerable<string> RankedNames =>
        Maps.Where(map => map.RankedRotation).Select(map => map.Name);

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out JsonElement value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out int number) && number == 1,
            JsonValueKind.String => value.GetString()?.Trim() == "1",
            JsonValueKind.True => true,
            _ => false,
        };
    }
}
