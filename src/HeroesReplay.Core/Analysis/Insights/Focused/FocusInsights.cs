using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Analysis.Insights.Focused;

/// <summary>
/// How long the spectator's camera followed each hero (#412): the focus calculators' pick for
/// every second from gates open to the core. The most followed hero was the most active one.
/// </summary>
public sealed record FocusInsights
{
    /// <summary>Most followed first.</summary>
    public IReadOnlyList<FocusShare> Players { get; init; } = Array.Empty<FocusShare>();

    public FocusShare Top { get; init; }
}

/// <summary>One player's followed seconds and their share of the match.</summary>
public sealed record FocusShare(int PlayerIndex, int Team, string Hero, int Seconds, double Share);
