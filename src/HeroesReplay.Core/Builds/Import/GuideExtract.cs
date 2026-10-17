using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HeroesReplay.Core.Builds.Import;

/// <summary>
/// What the <c>icy-veins-builds</c> skill reads from one guide page (#412), before it is checked
/// against the game's talents. A tier is the guide's yes/situational/no marks, one per talent in
/// the game's own column order (<c>"nys"</c>), plus the slugs of the talents it recommends.
/// </summary>
public sealed record GuideExtractHero
{
    public string Slug { get; init; }

    /// <summary>The guide's "last updated" text, such as <c>Oct 27, 2020</c>.</summary>
    public string Updated { get; init; }

    public IReadOnlyList<GuideExtractBuild> Builds { get; set; } = Array.Empty<GuideExtractBuild>();
}

public sealed record GuideExtractBuild
{
    public string Name { get; init; }

    public string Tag { get; init; }

    /// <summary>The talent calculator code after the <c>#</c>, such as <c>48.2!2232123</c>, or null.</summary>
    public string Calculator { get; init; }

    public IReadOnlyList<GuideExtractTier> Tiers { get; set; } = Array.Empty<GuideExtractTier>();
}

public sealed record GuideExtractTier
{
    /// <summary>The level the guide labels the tier with.</summary>
    public string Label { get; init; }

    /// <summary>One of <c>y</c>, <c>s</c>, <c>n</c> per talent in the tier, left to right.</summary>
    public string Marks { get; init; }

    /// <summary>The recommended talents' slugs (<c>calamity</c>), used to check the column order.</summary>
    public IReadOnlyList<string> Recommended { get; set; } = Array.Empty<string>();
}

public static class GuideExtractJson
{
    internal static readonly BuildsJsonContext Json = new(
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
    );

    public static IReadOnlyList<GuideExtractHero> Parse(string json) =>
        JsonSerializer.Deserialize(json, Json.GuideExtractHeroArray)
        ?? Array.Empty<GuideExtractHero>();
}
