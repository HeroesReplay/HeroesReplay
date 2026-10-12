using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Builds;
using HeroesReplay.Core.Analysis.Insights.Camps;
using HeroesReplay.Core.Analysis.Insights.Lanes;
using HeroesReplay.Core.Analysis.Insights.Players;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using HeroesReplay.Core.Builds.Import;
using HeroesReplay.Core.Clips;

namespace HeroesReplay.Core.YouTube.Metadata.Highlights;

/// <summary>
/// The description's insight lines (#412). No line starts with a label the library pass reads
/// (<c>Map:</c>, <c>Mode:</c>, <c>Rank:</c>, <c>Build:</c>, <c>Draft:</c>, <c>Featured:</c>), and
/// none says who won.
/// </summary>
public static class HighlightLines
{
    public const int MaxHighlights = 8;

    /// <summary>
    /// <c>Highlights:</c> with the pentakills first (as before), then the standouts, the MVP, a core
    /// race and a comeback. Null when there is none.
    /// </summary>
    public static string Highlights(
        HighlightContext context,
        IReadOnlyList<TeamKillClip> pentakills
    )
    {
        var parts = new List<string>();
        foreach (TeamKillClip clip in pentakills ?? new List<TeamKillClip>())
        {
            string kind = clip.WipedTeam ? "pentakill (team wipe)" : "pentakill";
            parts.Add(string.IsNullOrWhiteSpace(clip.Hero) ? kind : clip.Hero.Trim() + " " + kind);
        }

        PlayerLine mvp = context.Insights.Players.Mvp;
        bool mvpNamed = false;
        foreach (Standout standout in context.Insights.Players.Standouts)
        {
            PlayerLine line = standout.Player;
            string text = standout.Kind switch
            {
                StandoutKind.DeathlessCarry => $"{line.Hero} {line.Kda} with no deaths",
                StandoutKind.TeamKillLeader =>
                    $"{line.Hero} {line.Kda}, the {ReplaySnapshot.TeamName(line.Team)} kill leader",
                _ => $"{line.Hero} {line.Kda}",
            };
            if (mvp != null && mvp.PlayerIndex == line.PlayerIndex)
            {
                text += ", MVP";
                mvpNamed = true;
            }

            parts.Add(text);
        }

        if (mvp?.Hero != null && !mvpNamed)
        {
            parts.Add($"MVP {mvp.Hero} {mvp.Kda}");
        }

        if (context.Insights.Finish.CoreRace)
        {
            parts.Add("core race finish");
        }

        if (context.Insights.Comeback.Comeback)
        {
            parts.Add("insane comeback");
        }

        return parts.Count == 0
            ? null
            : "Highlights: " + string.Join("; ", parts.Take(MaxHighlights));
    }

    /// <summary><c>Solo lanes: Chen (Blue) vs Thrall (Red)</c>, High confidence only. Null otherwise.</summary>
    public static string SoloLanes(HighlightContext context)
    {
        if (!context.Settings.SoloLaneLine)
        {
            return null;
        }

        List<TeamLanes> sure = LaneHighlights.HighConfidence(context).ToList();
        return sure.Count switch
        {
            2 => $"Solo lanes: {Named(sure[0])} vs {Named(sure[1])}",
            1 => $"Solo lane: {Named(sure[0])}",
            _ => null,
        };
    }

    /// <summary><c>Camps: Blue 9 (4 on Red's side), Red 3 (0 on Blue's side)</c>. Null in ARAM or with no capture.</summary>
    public static string Camps(HighlightContext context)
    {
        CampInsights camps = context.Insights.Camps;
        if (!context.Settings.CampLine || camps.Teams.Count != 2 || camps.TotalCaptures == 0)
        {
            return null;
        }

        return "Camps: "
            + string.Join(
                ", ",
                camps.Teams.Select(team =>
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{ReplaySnapshot.TeamName(team.Team)} {team.Captures} ({team.EnemySideCaptures} on {ReplaySnapshot.TeamName(1 - team.Team)}'s side)"
                    )
                )
            );
    }

    /// <summary>
    /// One line per player whose talents fit a guide build well, each with the guide's link, under
    /// a heading that credits Icy Veins. Empty when no player qualifies.
    /// </summary>
    public static IReadOnlyList<string> TalentBuilds(HighlightContext context)
    {
        if (!context.Settings.TalentBuildLines)
        {
            return new string[0];
        }

        List<string> lines = context
            .Insights.Builds.Players.Where(build =>
                build.Score >= context.Settings.DescriptionBuildMinScore && build.Hero != null
            )
            .Select(Line)
            .ToList();
        if (lines.Count == 0)
        {
            return lines;
        }

        lines.Insert(
            0,
            $"Talent builds (closest {IcyVeins.Source} guide build, {IcyVeins.HeroesUrl}):"
        );
        return lines;
    }

    /// <summary>
    /// <c>Li-Ming (Blue): Teleport Build, 6 of 7 talents as recommended - link</c>, and the picks the
    /// build lists as situational when there are some (<c>3 of 6 as recommended, 3 situational</c>).
    /// </summary>
    private static string Line(PlayerBuild build)
    {
        string fit = $"{build.Recommended} of {build.Tiers} talents as recommended";
        if (build.Situational > 0)
        {
            fit += $", {build.Situational} situational";
        }

        return $"{build.Hero} ({ReplaySnapshot.TeamName(build.Team)}): {build.Build}, {fit} - {build.GuideUrl}";
    }

    private static string Named(TeamLanes team) =>
        $"{team.SoloLaner.Hero} ({ReplaySnapshot.TeamName(team.Team)})";
}
