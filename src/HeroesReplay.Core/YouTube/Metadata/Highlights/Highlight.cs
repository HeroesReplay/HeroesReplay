using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.YouTube.Metadata.Highlights;

/// <summary>What a full match title can lead with (#412). None of them says who won.</summary>
public enum HighlightKind
{
    /// <summary>One hero's pentakill: five killing blows of their own, never a shared wipe (#369).</summary>
    Pentakill,

    /// <summary>Both teams had lost keeps and the fallen team was still at the other core.</summary>
    CoreRace,

    /// <summary>The team whose core survived was far behind on buildings or levels.</summary>
    Comeback,

    /// <summary>No deaths and many kills.</summary>
    DeathlessCarry,

    /// <summary>Many kills and at most one death.</summary>
    KillLeader,

    /// <summary>Each team's solo laner, named only at High confidence.</summary>
    SoloLane,

    /// <summary>The game's MVP award, which either team can win.</summary>
    Mvp,

    /// <summary>The hero the spectator's camera followed most.</summary>
    MostFollowed,
}

/// <summary>
/// One title candidate. <see cref="Variants"/> run from the fullest wording to the shortest, so a
/// long title can shorten it before dropping it.
/// </summary>
/// <param name="Kind">The highlight's kind.</param>
/// <param name="Hero">The hero it is about, or null (a match shape, or two heroes).</param>
/// <param name="Variants">The wordings, longest first.</param>
/// <param name="Tag">A video tag for it, or null.</param>
public sealed record Highlight(
    HighlightKind Kind,
    string Hero,
    IReadOnlyList<string> Variants,
    string Tag
)
{
    public string Text => Variants.Count == 0 ? string.Empty : Variants[0];

    public bool IsAbout(string hero) =>
        Hero != null
        && hero != null
        && string.Equals(Hero, hero, StringComparison.OrdinalIgnoreCase);
}
