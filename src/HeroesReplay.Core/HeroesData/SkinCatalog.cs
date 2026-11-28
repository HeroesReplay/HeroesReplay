using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace HeroesReplay.Core.HeroesData;

/// <summary>
/// One hero skin from heroes-data2 <c>skindata</c>: its English name, the name of the skin it
/// tints (<c>Holy Holstein Moophisto</c> is a tint of <c>Moophisto</c>), rarity, features and
/// release day.
/// </summary>
public sealed record Skin(
    string Id,
    string Name,
    string BaseName,
    string Rarity,
    IReadOnlyList<string> Features,
    DateTime? ReleaseDate
)
{
    public const string Legendary = "Legendary";

    /// <summary>
    /// Features that change how the skin moves or sounds, not only its colours: themed
    /// animations, and altered or replaced voice over.
    /// </summary>
    public static readonly IReadOnlyList<string> AnimatedOrVoiced = new[]
    {
        "ThemedAnimations",
        "AlteredVO",
        "ReplacedVO",
    };

    public bool IsLegendary => string.Equals(Rarity, Legendary, StringComparison.OrdinalIgnoreCase);

    public bool IsAnimatedOrVoiced
    {
        get
        {
            if (Features == null)
            {
                return false;
            }

            foreach (string feature in Features)
            {
                foreach (string wanted in AnimatedOrVoiced)
                {
                    if (string.Equals(feature, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The skin came out within <paramref name="days"/> before <paramref name="played"/> (or on
    /// the day). Unknown dates, a zero window and skins released after the game are not new.
    /// </summary>
    public bool IsNew(DateTime? played, int days)
    {
        if (ReleaseDate == null || played == null || played.Value == default || days <= 0)
        {
            return false;
        }

        double age = (played.Value.Date - ReleaseDate.Value.Date).TotalDays;
        return age >= 0 && age <= days;
    }

    /// <summary>Legendary, animated or voiced, or new: the skins the metadata names.</summary>
    public bool IsNotable(DateTime? played, int newDays) =>
        IsLegendary || IsAnimatedOrVoiced || IsNew(played, newDays);
}

/// <summary>
/// The skins of the newest heroes-data2 build, by the id a replay stores for each player
/// (<c>Player.SkinAndSkinTint</c>, such as <c>MephistoMoophisto</c>). A default skin has no id.
/// </summary>
public sealed class SkinCatalog
{
    public static readonly SkinCatalog Empty = new(new Dictionary<string, Skin>());

    private readonly IReadOnlyDictionary<string, Skin> skins;

    private SkinCatalog(IReadOnlyDictionary<string, Skin> skins)
    {
        this.skins = skins;
    }

    public int Count => skins.Count;

    /// <summary>The skin, or null for a default skin or an id this build does not know.</summary>
    public Skin Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return skins.TryGetValue(id.Trim(), out Skin skin) ? skin : null;
    }

    /// <summary>
    /// Reads <c>skindata_&lt;build&gt;.json</c> and the English gamestrings (<c>items.skin.name</c>).
    /// A skin without an English name is left out: the metadata never shows a raw id.
    /// </summary>
    public static SkinCatalog Read(string skinDataJson, string gameStringsJson)
    {
        if (string.IsNullOrWhiteSpace(skinDataJson) || string.IsNullOrWhiteSpace(gameStringsJson))
        {
            return Empty;
        }

        Dictionary<string, string> names = Names(gameStringsJson);
        using JsonDocument data = JsonDocument.Parse(skinDataJson);
        if (
            !data.RootElement.TryGetProperty("items", out JsonElement items)
            || items.ValueKind != JsonValueKind.Object
        )
        {
            return Empty;
        }

        // A tint names its base skin in the base's variationSkinIds.
        var baseOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty item in items.EnumerateObject())
        {
            if (item.Value.TryGetProperty("variationSkinIds", out JsonElement variations))
            {
                foreach (string variation in Strings(variations))
                {
                    baseOf[variation] = item.Name;
                }
            }
        }

        var skins = new Dictionary<string, Skin>(StringComparer.Ordinal);
        foreach (JsonProperty item in items.EnumerateObject())
        {
            if (!names.TryGetValue(item.Name, out string name))
            {
                continue;
            }

            string baseName =
                baseOf.TryGetValue(item.Name, out string baseId)
                && names.TryGetValue(baseId, out string named)
                    ? named
                    : name;
            skins[item.Name] = new Skin(
                item.Name,
                name,
                baseName,
                Text(item.Value, "rarity"),
                item.Value.TryGetProperty("features", out JsonElement features)
                    ? Strings(features)
                    : Array.Empty<string>(),
                Day(Text(item.Value, "releaseDate"))
            );
        }

        return new SkinCatalog(skins);
    }

    private static Dictionary<string, string> Names(string gameStringsJson)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        using JsonDocument strings = JsonDocument.Parse(gameStringsJson);
        if (
            strings.RootElement.TryGetProperty("items", out JsonElement items)
            && items.TryGetProperty("skin", out JsonElement skin)
            && skin.TryGetProperty("name", out JsonElement name)
            && name.ValueKind == JsonValueKind.Object
        )
        {
            foreach (JsonProperty entry in name.EnumerateObject())
            {
                string value = entry.Value.ValueKind == JsonValueKind.String
                    ? entry.Value.GetString()?.Trim()
                    : null;
                if (!string.IsNullOrEmpty(value))
                {
                    names[entry.Name] = value;
                }
            }
        }

        return names;
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string[] Strings(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (JsonElement value in array.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                values.Add(value.GetString());
            }
        }

        return values.ToArray();
    }

    private static DateTime? Day(string text) =>
        DateTime.TryParseExact(
            text,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime day
        )
            ? day
            : null;
}
