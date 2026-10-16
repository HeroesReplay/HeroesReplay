using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.HeroFocus.Signatures;

/// <summary>
/// What a strong game on one hero looks like (#433): the end-of-game stats that should be high,
/// the ones that should be low, and the awards that count. A hero takes its role's lists unless
/// its own entry names them, and a build in <see cref="HeroSignatureEntry.BuildRoles"/> is judged
/// by another role's lists (Varian's Taunt builds by the Tank's).
/// </summary>
public sealed record HeroSignature(
    string Role,
    IReadOnlyList<string> Stats,
    IReadOnlyList<string> Low,
    IReadOnlyList<string> Awards,
    bool IgnoreDeaths
);

/// <summary>
/// The embedded signature table (<c>HeroFocus/Signatures/hero-signatures.json</c>): role defaults
/// and an entry for every hero. A hero is found by its unit id, then its English name.
/// </summary>
public sealed class HeroSignatures
{
    public const string ResourceName =
        "HeroesReplay.Core.HeroFocus.Signatures.hero-signatures.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly Dictionary<string, RoleSignature> roles = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Dictionary<string, HeroSignatureEntry> byUnit = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Dictionary<string, HeroSignatureEntry> byName = new(
        StringComparer.OrdinalIgnoreCase
    );

    public HeroSignatures(HeroSignatureFile file)
    {
        file ??= new HeroSignatureFile();
        foreach (KeyValuePair<string, RoleSignature> pair in file.Roles ?? new())
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
            {
                roles[pair.Key.Trim()] = pair.Value;
            }
        }

        Heroes = (file.Heroes ?? new List<HeroSignatureEntry>())
            .Where(entry => entry != null)
            .ToList();
        foreach (HeroSignatureEntry entry in Heroes)
        {
            if (!string.IsNullOrWhiteSpace(entry.UnitId))
            {
                byUnit.TryAdd(entry.UnitId.Trim(), entry);
            }

            if (!string.IsNullOrWhiteSpace(entry.Hero))
            {
                byName.TryAdd(entry.Hero.Trim(), entry);
            }
        }
    }

    public IReadOnlyList<HeroSignatureEntry> Heroes { get; }

    public IReadOnlyDictionary<string, RoleSignature> Roles => roles;

    public static HeroSignatures Embedded()
    {
        using Stream stream =
            typeof(HeroSignatures).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is not embedded.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static HeroSignatures Parse(string json) =>
        new(
            string.IsNullOrWhiteSpace(json)
                ? new HeroSignatureFile()
                : JsonSerializer.Deserialize<HeroSignatureFile>(json, Json)
        );

    /// <summary>The hero's entry: by unit id, then by English name. Null when it has none.</summary>
    public HeroSignatureEntry Find(string unitId, string heroName)
    {
        if (
            !string.IsNullOrWhiteSpace(unitId)
            && byUnit.TryGetValue(unitId.Trim(), out HeroSignatureEntry entry)
        )
        {
            return entry;
        }

        return
            !string.IsNullOrWhiteSpace(heroName) && byName.TryGetValue(heroName.Trim(), out entry)
            ? entry
            : null;
    }

    public HeroSignature For(Hero hero, string build = null) =>
        hero == null ? null : For(hero.UnitId, hero.Name, hero.Role, build);

    /// <summary>
    /// The hero's signature for the build played. Null when neither the hero's entry nor its
    /// role has stats to judge by.
    /// </summary>
    public HeroSignature For(string unitId, string heroName, string role, string build = null)
    {
        HeroSignatureEntry entry = Find(unitId, heroName);
        string judgedAs = role;
        if (
            entry?.BuildRoles != null
            && !string.IsNullOrWhiteSpace(build)
            && entry.BuildRoles.TryGetValue(build.Trim(), out string buildRole)
            && !string.IsNullOrWhiteSpace(buildRole)
        )
        {
            judgedAs = buildRole.Trim();
            entry = entry with { Stats = null, Low = null, Awards = null };
        }

        RoleSignature defaults =
            !string.IsNullOrWhiteSpace(judgedAs)
            && roles.TryGetValue(judgedAs.Trim(), out RoleSignature found)
                ? found
                : null;
        IReadOnlyList<string> stats = entry?.Stats ?? defaults?.Stats;
        if (stats == null || stats.Count == 0)
        {
            return null;
        }

        return new HeroSignature(
            judgedAs,
            stats,
            entry?.Low ?? defaults?.Low ?? Array.Empty<string>(),
            entry?.Awards ?? defaults?.Awards ?? Array.Empty<string>(),
            entry?.IgnoreDeaths == true
        );
    }
}

public sealed class HeroSignatureFile
{
    public string About { get; set; }
    public Dictionary<string, RoleSignature> Roles { get; set; } = new();
    public List<HeroSignatureEntry> Heroes { get; set; } = new();
}

public sealed class RoleSignature
{
    public IReadOnlyList<string> Stats { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Low { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Awards { get; set; } = Array.Empty<string>();
}

/// <summary>One hero's entry. A null list means the role's list.</summary>
public sealed record HeroSignatureEntry
{
    public string Hero { get; init; }
    public string UnitId { get; init; }
    public IReadOnlyList<string> Stats { get; init; }
    public IReadOnlyList<string> Low { get; init; }
    public IReadOnlyList<string> Awards { get; init; }

    /// <summary>Deaths and outnumbered deaths do not count against this hero (Murky).</summary>
    public bool IgnoreDeaths { get; init; }

    /// <summary>Builds judged as another role (build name → role).</summary>
    public IReadOnlyDictionary<string, string> BuildRoles { get; init; }

    /// <summary>Why these stats: a fact about the hero's kit, never guide text.</summary>
    public string Why { get; init; }
}
