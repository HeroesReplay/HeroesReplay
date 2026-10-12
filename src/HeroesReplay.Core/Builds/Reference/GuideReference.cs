using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Builds.Reference;

/// <summary>
/// The talent-build reference (#412): for every hero, the guide's link and its named builds, each
/// as the talents it recommends per tier. Only facts are kept: build names, tags, talent ids,
/// links and dates. No guide text. <c>heroesreplay builds import</c> writes it from the skill
/// <c>icy-veins-builds</c>; the product never fetches the guides itself.
/// </summary>
public sealed record GuideReference
{
    /// <summary>Who the builds come from, for the attribution line.</summary>
    public string Source { get; init; }

    /// <summary>The source's hero guides index.</summary>
    public string SourceUrl { get; init; }

    /// <summary>The day the guides were read.</summary>
    public DateOnly? CheckedAt { get; init; }

    /// <summary>The heroes-data build the talents were checked against, such as <c>2.57.0.98304</c>.</summary>
    public string GameVersion { get; init; }

    public IReadOnlyList<HeroGuide> Heroes { get; init; } = Array.Empty<HeroGuide>();
}

/// <summary>One hero's guide: its link, the day the guide was last updated, and its builds.</summary>
public sealed record HeroGuide
{
    /// <summary>The hero's English name, as the heroes-data catalog has it.</summary>
    public string Hero { get; init; }

    /// <summary>The hero's catalog unit id (<c>HeroWizard</c>). It is the same in every game language.</summary>
    public string UnitId { get; init; }

    /// <summary>The source's hero slug (<c>li-ming</c>).</summary>
    public string Slug { get; init; }

    public string GuideUrl { get; init; }

    /// <summary>The guide's own "last updated" day. Old guides can predate talent changes.</summary>
    public DateOnly? GuideUpdated { get; init; }

    /// <summary>The guide's builds in page order.</summary>
    public IReadOnlyList<GuideBuild> Builds { get; init; } = Array.Empty<GuideBuild>();
}

/// <summary>One named build of a guide, such as Li-Ming's Teleport Build.</summary>
public sealed record GuideBuild
{
    /// <summary>The build's name as the guide writes it (<c>Teleport Build</c>).</summary>
    public string Name { get; init; }

    /// <summary>The guide's tag: <see cref="GuideTags"/>.</summary>
    public string Tag { get; init; }

    /// <summary>The source's talent calculator link with this build's talents, or null.</summary>
    public string CalculatorUrl { get; init; }

    /// <summary>Seven tiers in level order.</summary>
    public IReadOnlyList<GuideTier> Tiers { get; init; } = Array.Empty<GuideTier>();
}

/// <summary>
/// One tier of a build: the talents it recommends and the ones it calls situational, by catalog
/// talent id. A stale tier no longer matched the game's talents when it was imported and is
/// not scored.
/// </summary>
public sealed record GuideTier
{
    /// <summary>The level the guide labels the tier with (Chromie's tiers unlock at 1, 2, 5, 8, 11, 14, 18).</summary>
    public int Level { get; init; }

    public IReadOnlyList<string> Recommended { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Situational { get; init; } = Array.Empty<string>();

    public bool Stale { get; init; }
}
