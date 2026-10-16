using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Builds;
using HeroesReplay.Core.Analysis.Insights.Players;
using HeroesReplay.Core.Clips;

namespace HeroesReplay.Core.YouTube.Metadata.Highlights;

/// <summary>Everything a highlight source reads: the insights, the pentakills, and the settings.</summary>
public sealed class HighlightContext
{
    public HighlightContext(
        MatchInsights insights,
        IReadOnlyList<TeamKillClip> pentakills,
        HighlightSettings settings
    )
    {
        Insights = insights ?? new MatchInsights();
        Pentakills = pentakills ?? Array.Empty<TeamKillClip>();
        Settings = settings ?? new HighlightSettings();
    }

    public MatchInsights Insights { get; }

    /// <summary>Individual pentakills, one hero each (#369).</summary>
    public IReadOnlyList<TeamKillClip> Pentakills { get; }

    public HighlightSettings Settings { get; }

    /// <summary>The player's score line, or null.</summary>
    public PlayerLine Line(int playerIndex) =>
        Insights.Players.Lines.FirstOrDefault(line => line.PlayerIndex == playerIndex);

    /// <summary>The hero's score line, or null when no player or two players have the hero.</summary>
    public PlayerLine LineFor(string hero)
    {
        List<PlayerLine> lines = Insights
            .Players.Lines.Where(line =>
                string.Equals(line.Hero, hero, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
        return lines.Count == 1 ? lines[0] : null;
    }

    /// <summary>
    /// The hero with its build's short name when the build is strong, not ambiguous, and not a
    /// plain name: <c>Teleport Li-Ming</c>. Otherwise null.
    /// </summary>
    public string BuildLabel(int playerIndex, string hero)
    {
        PlayerBuild build = Insights.Builds.Players.FirstOrDefault(player =>
            player.PlayerIndex == playerIndex
        );
        if (build == null || build.Ambiguous || build.Score < Settings.TitleBuildMinScore)
        {
            return null;
        }

        return Settings.BuildLabel(build.Build, hero);
    }
}
