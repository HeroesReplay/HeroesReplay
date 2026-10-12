using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Builds.Played;

/// <summary>
/// The talents one player picked in a replay, as the hero's catalog talents (#412). At most one
/// pick per tier, ordered by tier. A game that ends before level 20 has fewer than seven.
/// </summary>
public sealed record PlayedBuild
{
    public PlayedBuild(Hero hero, IReadOnlyList<HeroTalent> picks)
    {
        Hero = hero ?? throw new ArgumentNullException(nameof(hero));
        Picks = picks ?? Array.Empty<HeroTalent>();
    }

    public Hero Hero { get; }

    public IReadOnlyList<HeroTalent> Picks { get; }

    /// <summary>The pick in <paramref name="tier"/> (0 to 6), or null when the player has none.</summary>
    public HeroTalent At(int tier) => Picks.FirstOrDefault(pick => pick.Tier == tier);
}
