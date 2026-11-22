using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HeroesReplay.Core.Clips;

namespace HeroesReplay.Core.YouTube.Metadata;

/// <summary>
/// A clip's description for viewers: a sentence or two about what happens, written like someone
/// telling a friend about the play, not a log line. No seconds, counts or the reasons the clip was
/// picked (those stay in <see cref="TeamKillClip.Description"/> for the clip index and the logs).
/// Like <see cref="ClipTitle"/>, the strongest fact wins and the replay id picks among its lines,
/// so the same clip always gets the same words.
/// </summary>
public static class ClipDescription
{
    /// <summary>What a bstep is, for viewers who have never seen one.</summary>
    public const string BStepExplainer =
        "Bstepping: starting your Hearthstone and cancelling it over and over, the classic Heroes of the Storm taunt.";

    /// <summary>The viewer lines, or the clip's own line when the clip is of no known kind.</summary>
    public static IReadOnlyList<string> Compose(TeamKillClip clip, string map, string replayId)
    {
        ClipTitle.Facts facts = ClipTitle.Facts.Of(clip);
        var context = new Context(facts, clip.NamesTeam, map);
        List<Line> eligible = Lines
            .Where(line => line.Kind == facts.Kind && line.When(context))
            .ToList();
        if (eligible.Count == 0 || string.IsNullOrWhiteSpace(facts.Hero))
        {
            return string.IsNullOrWhiteSpace(clip.Description)
                ? Array.Empty<string>()
                : new[] { clip.Description };
        }

        int top = eligible.Min(line => line.Tier);
        List<Line> pool = eligible.Where(line => line.Tier == top).ToList();
        ulong hash = ClipTitle.Hash(
            string.Join(
                ":",
                replayId ?? string.Empty,
                clip.Kind ?? string.Empty,
                clip.FirstDeathSecond.ToString(CultureInfo.InvariantCulture),
                clip.Hero
            )
        );
        var lines = new List<string> { pool[(int)(hash % (ulong)pool.Count)].Text(context) };
        if (facts.Kind == ClipTitle.ClipKind.BStep)
        {
            lines.Add(BStepExplainer);
        }

        return lines;
    }

    /// <summary>The facts, plus the wording helpers the lines share.</summary>
    internal sealed class Context
    {
        public Context(ClipTitle.Facts facts, bool namesTeam, string map)
        {
            F = facts;
            NamesTeam = namesTeam;
            Map = string.IsNullOrWhiteSpace(map) ? null : map.Trim();
        }

        public ClipTitle.Facts F { get; }

        public bool NamesTeam { get; }

        public string Map { get; }

        public string Hero => F.Hero;

        /// <summary><c> on Cursed Hollow</c>, or nothing.</summary>
        public string OnMap => Map == null ? string.Empty : " on " + Map;

        /// <summary><c> for Blue</c>, or nothing when the clip already names the team.</summary>
        public string ForTeam => NamesTeam || F.Team == null ? string.Empty : " for " + F.Team;
    }

    internal sealed record Line(
        ClipTitle.ClipKind Kind,
        int Tier,
        Func<Context, bool> When,
        Func<Context, string> Text
    );

    private static readonly Func<Context, bool> Always = _ => true;

    internal static readonly IReadOnlyList<Line> Lines = new List<Line>
    {
        // Boss steal.
        new(
            ClipTitle.ClipKind.Steal,
            0,
            c => c.F.KilledByOther && c.F.Other != null && c.F.Team != null,
            c =>
                $"{c.F.Other} fought for the boss and took it down... and {c.F.Team} walked off with it anyway. Brutal."
        ),
        new(
            ClipTitle.ClipKind.Steal,
            0,
            c => c.F.KilledByOther && c.F.Other != null && c.F.Team != null,
            c =>
                $"{c.F.Other} finished the boss, but {c.F.Team} was waiting to snatch it. Heartbreaking for one side, glorious for the other."
        ),
        new(
            ClipTitle.ClipKind.Steal,
            1,
            c => c.F.LevelsBehind >= 2,
            c =>
                $"Behind, but not out of it: {c.Hero} sneaks in and steals the boss{c.ForTeam}{c.OnMap}. Never give up on a game."
        ),
        new(
            ClipTitle.ClipKind.Steal,
            2,
            c => c.F.StarterMost >= 3 && c.F.Other != null,
            c =>
                $"{c.F.Other} brought the whole squad to the boss, and {c.Hero} still found a way to take it."
        ),
        new(ClipTitle.ClipKind.Steal, 3, Always, c =>
            $"{c.Hero} slips into the boss pit at just the right moment and takes the boss{c.ForTeam}{c.OnMap}."
        ),
        new(ClipTitle.ClipKind.Steal, 3, Always, c =>
            $"Never leave a boss half-finished. {c.Hero} makes them pay{c.OnMap}."
        ),
        new(ClipTitle.ClipKind.Steal, 3, Always, c =>
            $"Why do the work when you can just take it? {c.Hero} with the cheeky boss steal{c.OnMap}."
        ),
        // Boss defended.
        new(
            ClipTitle.ClipKind.Defended,
            0,
            c => c.F.KilledByOther && c.F.Other != null && c.F.Team != null,
            c =>
                $"{c.F.Other} went for the steal, but {c.F.Team} held their ground and kept the boss."
        ),
        new(
            ClipTitle.ClipKind.Defended,
            1,
            c => c.F.PitDeaths >= 3,
            c =>
                $"It all kicks off at the boss pit, and {c.Hero} comes out on top to secure it{c.ForTeam}."
        ),
        new(ClipTitle.ClipKind.Defended, 2, Always, c =>
            $"A tense fight at the boss, and {c.Hero} holds their nerve to secure it{c.ForTeam}."
        ),
        new(ClipTitle.ClipKind.Defended, 2, Always, c =>
            $"The enemy wanted this boss. {c.Hero} had other plans{c.OnMap}."
        ),
        // Bstep.
        new(
            ClipTitle.ClipKind.BStep,
            0,
            c => c.F.VictimCount > 0 && c.F.DiedAfter,
            c =>
                $"{c.Hero} just had to show off over {c.F.Victims}... and the game noticed. Instant karma."
        ),
        new(
            ClipTitle.ClipKind.BStep,
            1,
            c => c.F.VictimCount > 0,
            c =>
                $"{c.Hero} takes down {c.F.Victims} and celebrates the only way they know how: a cheeky bstep dance."
        ),
        new(
            ClipTitle.ClipKind.BStep,
            1,
            c => c.F.VictimCount > 0,
            c => $"Bad manners or pure style? {c.Hero} bsteps over {c.F.Victims}. You decide."
        ),
        new(
            ClipTitle.ClipKind.BStep,
            2,
            c => c.F.DiedAfter,
            c => $"A little bstep showboating from {c.Hero}, and it does not end well."
        ),
        new(
            ClipTitle.ClipKind.BStep,
            3,
            c => c.F.AllyBefore,
            c =>
                $"A teammate goes down, and {c.Hero}'s first thought is... a bstep? Interesting priorities."
        ),
        new(ClipTitle.ClipKind.BStep, 4, Always, c =>
            $"{c.Hero} breaks out the bstep{c.OnMap}. Too easy?"
        ),
        // Kills.
        new(
            ClipTitle.ClipKind.Penta,
            0,
            c => c.F.Wiped,
            c => $"{c.Hero} takes on the whole enemy team and leaves no one standing. Pentakill!"
        ),
        new(ClipTitle.ClipKind.Penta, 1, Always, c =>
            $"Five kills, one {c.Hero}. A pentakill{c.OnMap} worth watching twice."
        ),
        new(ClipTitle.ClipKind.Quadra, 0, Always, c =>
            $"{c.Hero} tears through the fight for four kills, so close to the penta!"
        ),
        new(ClipTitle.ClipKind.Triple, 0, Always, c =>
            $"{c.Hero} cleans up the fight with a triple kill{c.OnMap}."
        ),
    };
}
