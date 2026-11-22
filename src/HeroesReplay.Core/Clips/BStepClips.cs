using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.BSteps;

namespace HeroesReplay.Core.Clips;

/// <summary>
/// Bstepping around a death (<see cref="BStepAnalyzer"/>): a hero hearths and cancels it by moving,
/// over and over, right after a kill or right before getting caught. Bad manners between two or
/// more players, so drama worth a clip. Only a bstep with a linked death is cut: one in the fountain
/// or after a quiet push shows nothing. The clip runs from the earlier of the first death and the
/// bstep to the later of the last death and the bstep, named after the bstepping hero.
/// <para>
/// At most <see cref="MaxClips"/> a replay: a bstep whose deaths involve the bstepper or an enemy
/// first (an ally falling next to them is weaker drama), then the most linked deaths, the most
/// cycles, the earliest; none that overlaps a kill or boss clip, since that moment is already cut.
/// Like a triple kill, never Notable. The camera must have the bstepper on screen
/// (<see cref="TeamKillClip.OnScreen"/>) for most of the bstep and its last moment
/// (<see cref="ClipCamera"/>): the movement is small, and near is not enough.
/// </para>
/// </summary>
public static class BStepClips
{
    public const string Kind = "bstep";

    /// <summary>
    /// The most bstep clips one replay is cut into. On 250 corpus games about half had a bstep
    /// with a linked death, some several, so one a replay keeps them rare.
    /// </summary>
    public const int MaxClips = 1;

    public const int LeadSeconds = 14;

    public const int TailSeconds = 14;

    /// <summary>A clip shows at most this much before the bstep starts, or after it ends.</summary>
    public const int MaxReachSeconds = 15;

    public static IReadOnlyList<TeamKillClip> Select(
        BStepInsights insights,
        IReadOnlyList<TeamKillClip> otherClips = null,
        Func<TeamKillClip, bool> onCamera = null
    )
    {
        if (insights?.BSteps == null || insights.BSteps.Count == 0)
        {
            return Array.Empty<TeamKillClip>();
        }

        var clips = new List<TeamKillClip>();
        foreach (
            BStep step in insights
                .BSteps.Where(step =>
                    step.Deaths?.Count > 0 && !string.IsNullOrWhiteSpace(step.Hero)
                )
                .OrderByDescending(step => step.Deaths.Any(Involves))
                .ThenByDescending(step => step.Deaths.Count)
                .ThenByDescending(step => step.Cycles)
                .ThenBy(step => step.StartSecond)
        )
        {
            TeamKillClip clip = Clip(step);
            if (
                (otherClips ?? Array.Empty<TeamKillClip>()).Any(other => Overlaps(other, clip))
                || clips.Any(other => Overlaps(other, clip))
                || (onCamera != null && !onCamera(clip))
            )
            {
                continue;
            }

            clips.Add(clip);
            if (clips.Count == MaxClips)
            {
                break;
            }
        }

        clips.Sort((left, right) => left.HudStartSecond.CompareTo(right.HudStartSecond));
        return clips;
    }

    internal static TeamKillClip Clip(BStep step)
    {
        int first = Math.Max(
            step.StartSecond - MaxReachSeconds,
            Math.Min(step.StartSecond, step.Deaths.Min(death => death.Second))
        );
        int last = Math.Min(
            step.EndSecond + MaxReachSeconds,
            Math.Max(step.EndSecond, step.Deaths.Max(death => death.Second))
        );
        return new TeamKillClip(
            Kind,
            step.Hero,
            // The camera check covers the bstep itself: the deaths are context.
            step.StartSecond,
            step.EndSecond,
            Math.Max(0, first - LeadSeconds),
            last + TailSeconds,
            Description(step),
            Player: step.PlayerIndex,
            OnScreen: true
        )
        {
            Facts = new ClipFacts { BStep = step },
        };
    }

    /// <summary>The clip's line: <c>Sonya bsteps over Muradin</c>, <c>Murky bsteps, then dies</c>.</summary>
    public static string Description(BStep step)
    {
        string hero = step.Hero;
        BStepDeath own = step.Deaths.FirstOrDefault(death => death.Kind == BStepDeathKind.Bstepper);
        List<BStepDeath> before = step
            .Deaths.Where(death =>
                !death.After
                && death.Kind is BStepDeathKind.KilledByBstepper or BStepDeathKind.Enemy
                && !string.IsNullOrWhiteSpace(death.Hero)
            )
            .ToList();
        string text;
        if (before.Count > 0)
        {
            text =
                $"{hero} bsteps over {Join(before.Select(death => death.Hero).Distinct().ToList())}";
        }
        else if (step.Deaths.Any(death => death.Kind == BStepDeathKind.Ally && !death.After))
        {
            text = $"{hero} bsteps after an ally falls";
        }
        else
        {
            text = $"{hero} bsteps";
        }

        BStepDeath next = step
            .Deaths.Where(death => death.After && death.Kind != BStepDeathKind.Bstepper)
            .FirstOrDefault(death => !string.IsNullOrWhiteSpace(death.Hero));
        if (own != null && own.After)
        {
            text += ", then dies";
        }
        else if (next != null)
        {
            text += $", then {next.Hero} dies";
        }

        return text;
    }

    private static string Join(IReadOnlyList<string> names) =>
        names.Count switch
        {
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
        };

    /// <summary>The bstepper died or got the kill, or an enemy fell next to them.</summary>
    private static bool Involves(BStepDeath death) => death.Kind != BStepDeathKind.Ally;

    private static bool Overlaps(TeamKillClip left, TeamKillClip right) =>
        left.HudStartSecond <= right.HudEndSecond && right.HudStartSecond <= left.HudEndSecond;
}
