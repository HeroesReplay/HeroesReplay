using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Bosses;
using HeroesReplay.Core.Analysis.Insights.BSteps;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using HeroesReplay.Core.Clips;

namespace HeroesReplay.Core.YouTube.Metadata;

/// <summary>
/// A clip's title: <c>Hook - Map - ReplayId</c>, the hook picked from the lines below whose facts
/// the clip proves (<see cref="ClipFacts"/>, its blows), then the game's name when it fits
/// (<see cref="GameNameSettings"/>). Every hook names the hero (or the team) and the plain kind
/// word (<c>bstep</c>, <c>triple kill</c>, <c>quadra kill</c>, <c>pentakill</c>, <c>boss</c>), so search
/// still finds it and nothing claims more than the clip shows. Lines have tiers: the strongest fact
/// the clip has wins, and among the lines of that tier the replay id picks one, so the same clip
/// always gets the same title. Over the budget the emoji goes, then the next line, and last the
/// flat <c>Hero - kind - Map - ReplayId</c> title.
/// </summary>
public static class ClipTitle
{
    public const int MaxCharacters = 100;

    public static string Compose(
        TeamKillClip clip,
        string map,
        string replayId,
        YouTubeTitleSettings titles,
        string marker
    )
    {
        int budget = Budget(marker);
        string flat = Join(clip.Hero, TeamKillClips.Title(clip), map, replayId);
        string title =
            titles?.Clips?.Enabled == false
                ? flat
                : Pick(clip, map, replayId, titles?.Clips?.Emoji != false, budget) ?? flat;
        GameNameSettings game = titles?.GameName;
        return game?.Clips == true ? game.Append(title, budget) : title;
    }

    /// <summary>YouTube's 100 characters, less the marker and its space.</summary>
    public static int Budget(string marker) =>
        string.IsNullOrWhiteSpace(marker)
            ? MaxCharacters
            : MaxCharacters - marker.Trim().Length - 1;

    /// <summary>The hook title, or null when no line fits or the clip names no one.</summary>
    internal static string Pick(
        TeamKillClip clip,
        string map,
        string replayId,
        bool emoji,
        int budget
    )
    {
        if (string.IsNullOrWhiteSpace(clip.Hero))
        {
            return null;
        }

        Facts facts = Facts.Of(clip);
        List<Line> eligible = Lines
            .Where(line => line.Kind == facts.Kind && line.When(facts))
            .ToList();
        if (eligible.Count == 0)
        {
            return null;
        }

        int top = eligible
            .Where(line => line.Tier >= 0)
            .Select(line => line.Tier)
            .DefaultIfEmpty(-1)
            .Min();
        List<Line> pool = eligible.Where(line => line.Tier == top || line.Tier < 0).ToList();
        List<Line> rest = eligible.Except(pool).OrderBy(line => line.Tier).ToList();
        ulong hash = Hash(
            string.Join(
                ":",
                replayId ?? string.Empty,
                clip.Kind ?? string.Empty,
                clip.FirstDeathSecond.ToString(CultureInfo.InvariantCulture),
                clip.Hero
            )
        );
        int start = (int)(hash % (ulong)pool.Count);
        foreach (Line line in pool.Skip(start).Concat(pool.Take(start)).Concat(rest))
        {
            string hook = Clean(line.Text(facts));
            if (hook == null)
            {
                continue;
            }

            string withEmoji =
                emoji && line.Emoji.Length > 0
                    ? hook + " " + line.Emoji[(int)(hash / 7 % (ulong)line.Emoji.Length)]
                    : null;
            foreach (string candidate in new[] { withEmoji, hook })
            {
                if (candidate == null)
                {
                    continue;
                }

                string title = Join(candidate, map, replayId);
                if (title.Length <= budget)
                {
                    return title;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A hook the title can use: never a ` - ` (the title's separator) and never all digits, which
    /// the replay id reader would take for the id (<c>YouTubeReplayMatch.TryReadTitleId</c>).
    /// </summary>
    private static string Clean(string hook)
    {
        if (string.IsNullOrWhiteSpace(hook))
        {
            return null;
        }

        string text = hook.Trim();
        if (text.Contains(" - ", StringComparison.Ordinal) || text.All(char.IsDigit))
        {
            return null;
        }

        return text;
    }

    private static string Join(params string[] parts) =>
        string.Join(
            " - ",
            parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim())
        );

    /// <summary>FNV-1a: stable across runs and machines, unlike <see cref="string.GetHashCode()"/>.</summary>
    private static ulong Hash(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }

        return hash;
    }

    internal enum ClipKind
    {
        Other,
        BStep,
        Triple,
        Quadra,
        Penta,
        Steal,
        Defended,
    }

    /// <summary>What the lines may say about one clip, read once.</summary>
    internal sealed class Facts
    {
        public ClipKind Kind { get; init; }

        public string Hero { get; init; }

        /// <summary>The plain kind word: <c>triple kill</c>, <c>quadra kill</c>, <c>pentakill</c>.</summary>
        public string KindWord { get; init; }

        /// <summary>From the first blow to the last, in seconds.</summary>
        public int Seconds { get; init; }

        public bool Wiped { get; init; }

        public int Cycles { get; init; }

        public int BStepSeconds { get; init; }

        /// <summary>The enemies who fell before the bstep: <c>Muradin</c>, <c>Jaina and Uther</c>, <c>3 heroes</c>.</summary>
        public string Victims { get; init; }

        public int VictimCount { get; init; }

        public bool DiedAfter { get; init; }

        public bool AllyBefore { get; init; }

        /// <summary>The capturing team (<c>Blue</c>), or for a defended boss the team that kept it.</summary>
        public string Team { get; init; }

        /// <summary>The team the boss was taken from, or that tried to take it.</summary>
        public string Other { get; init; }

        public int? LevelsBehind { get; init; }

        public bool KilledByOther { get; init; }

        /// <summary>The most of the starting team's heroes on the boss at once, or 0.</summary>
        public int StarterMost { get; init; }

        public int CapturerMost { get; init; }

        public int StarterGroupSeconds { get; init; }

        /// <summary>The pit is in the starting team's half.</summary>
        public bool StarterHalf { get; init; }

        public int PitDeaths { get; init; }

        public int TogetherSeconds { get; init; }

        public bool IsHero(params string[] names) =>
            names.Any(name => string.Equals(Hero, name, StringComparison.OrdinalIgnoreCase));

        public static Facts Of(TeamKillClip clip)
        {
            string hero = clip.Hero?.Trim();
            int blows = clip.Kills?.Count ?? 0;
            int seconds = Math.Max(0, clip.LastDeathSecond - clip.FirstDeathSecond);
            switch (clip.Kind)
            {
                case TeamKillClips.PentakillKind:
                    return new Facts
                    {
                        Kind = ClipKind.Penta,
                        Hero = hero,
                        KindWord = "pentakill",
                        Seconds = seconds,
                        Wiped = clip.WipedTeam,
                    };
                case TeamKillClips.TripleKillKind:
                    return new Facts
                    {
                        Kind = blows >= 4 ? ClipKind.Quadra : ClipKind.Triple,
                        Hero = hero,
                        KindWord = blows >= 4 ? "quadra kill" : "triple kill",
                        Seconds = seconds,
                    };
                case BStepClips.Kind:
                    return BStepFacts(hero, clip.Facts?.BStep);
                case BossFightClips.StealKind:
                case BossFightClips.DefendedKind:
                    return BossFacts(hero, clip);
                default:
                    return new Facts { Kind = ClipKind.Other, Hero = hero };
            }
        }

        private static Facts BStepFacts(string hero, BStep step)
        {
            if (step == null)
            {
                return new Facts { Kind = ClipKind.BStep, Hero = hero };
            }

            IReadOnlyList<BStepDeath> deaths = step.Deaths ?? Array.Empty<BStepDeath>();
            List<string> victims = deaths
                .Where(death =>
                    !death.After
                    && death.Kind is BStepDeathKind.KilledByBstepper or BStepDeathKind.Enemy
                    && !string.IsNullOrWhiteSpace(death.Hero)
                )
                .Select(death => death.Hero.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new Facts
            {
                Kind = ClipKind.BStep,
                Hero = hero,
                Cycles = step.Cycles,
                BStepSeconds = Math.Max(1, step.EndSecond - step.StartSecond),
                Victims = victims.Count switch
                {
                    0 => null,
                    1 => victims[0],
                    2 => $"{victims[0]} and {victims[1]}",
                    _ => $"{victims.Count} heroes",
                },
                VictimCount = victims.Count,
                DiedAfter = deaths.Any(death =>
                    death.Kind == BStepDeathKind.Bstepper && death.After
                ),
                AllyBefore = deaths.Any(death => death.Kind == BStepDeathKind.Ally && !death.After),
            };
        }

        private static Facts BossFacts(string hero, TeamKillClip clip)
        {
            bool steal = clip.Kind == BossFightClips.StealKind;
            BossFight fight = clip.Facts?.Boss;
            if (fight == null)
            {
                return new Facts { Kind = steal ? ClipKind.Steal : ClipKind.Defended, Hero = hero };
            }

            int? starter = fight.Starter;
            BossFightTeam starting = TeamAt(fight, starter);
            BossFightTeam capturing = TeamAt(fight, fight.Team);
            int? other = fight.KilledByOtherTeam ? fight.KillerTeam : starter;
            if (other == fight.Team)
            {
                other = null;
            }

            return new Facts
            {
                Kind = steal ? ClipKind.Steal : ClipKind.Defended,
                Hero = hero,
                Team = ReplaySnapshot.TeamName(fight.Team),
                Other = other is int team ? ReplaySnapshot.TeamName(team) : null,
                LevelsBehind = clip.Facts.LevelsBehind,
                KilledByOther = fight.KilledByOtherTeam,
                StarterMost = starter != fight.Team ? starting?.MostHeroes ?? 0 : 0,
                CapturerMost = capturing?.MostHeroes ?? 0,
                StarterGroupSeconds = starter != fight.Team ? starting?.GroupSeconds ?? 0 : 0,
                StarterHalf = starter is int side && side != fight.Team && fight.Side == side,
                PitDeaths = fight.Teams?.Sum(entry => entry.Deaths) ?? 0,
                TogetherSeconds = fight.TogetherSeconds,
            };
        }

        private static BossFightTeam TeamAt(BossFight fight, int? team) =>
            team is int index && fight.Teams != null
                ? fight.Teams.FirstOrDefault(entry => entry.Team == index)
                : null;
    }

    /// <summary>
    /// One hook. <see cref="Tier"/> 0 is the strongest fact; a negative tier joins whichever tier
    /// the clip reaches (a hero's own line, or a fact that fits any of them).
    /// </summary>
    internal sealed class Line
    {
        public Line(
            ClipKind kind,
            int tier,
            Func<Facts, bool> when,
            Func<Facts, string> text,
            params string[] emoji
        )
        {
            Kind = kind;
            Tier = tier;
            When = when;
            Text = text;
            Emoji = emoji ?? Array.Empty<string>();
        }

        public ClipKind Kind { get; }

        public int Tier { get; }

        public Func<Facts, bool> When { get; }

        public Func<Facts, string> Text { get; }

        public string[] Emoji { get; }
    }

    private static readonly string[] Grave = { "😤", "🤡", "😂" };
    private static readonly string[] Karma = { "💀", "😂" };
    private static readonly string[] Oops = { "🙃", "🤡" };
    private static readonly string[] Fire = { "🔥", "💥", "☠️" };

    private static readonly Func<Facts, bool> Always = _ => true;

    /// <summary>A hero's own line for each kill kind, from what the community says about them.</summary>
    private static IEnumerable<Line> HeroKillLines(string[] heroes, Func<Facts, string> text)
    {
        foreach (ClipKind kind in new[] { ClipKind.Triple, ClipKind.Quadra, ClipKind.Penta })
        {
            yield return new Line(
                kind,
                -1,
                f => f.IsHero(heroes),
                text,
                kind == ClipKind.Penta ? Fire : Array.Empty<string>()
            );
        }
    }

    internal static readonly IReadOnlyList<Line> Lines = BuildLines();

    private static List<Line> BuildLines()
    {
        var lines = new List<Line>
        {
            // Bstep: bad manners between players, so the title gets sass.
            new(
                ClipKind.BStep,
                0,
                f => f.VictimCount > 0 && f.DiedAfter,
                f => $"{f.Hero} bsteps over {f.Victims}, then dies. Karma",
                Karma
            ),
            new(
                ClipKind.BStep,
                0,
                f => f.VictimCount > 0 && f.DiedAfter,
                f => $"Instant karma: {f.Hero} bsteps over {f.Victims} and dies",
                Karma
            ),
            new(
                ClipKind.BStep,
                1,
                f => f.VictimCount == 1,
                f => $"{f.Hero} bsteps on {f.Victims}'s grave",
                Grave
            ),
            new(
                ClipKind.BStep,
                1,
                f => f.VictimCount > 0,
                f => $"Bstepping over {f.Victims}?! {f.Hero} has no manners",
                Grave
            ),
            new(
                ClipKind.BStep,
                1,
                f => f.VictimCount > 0,
                f => $"{f.Hero} bsteps over {f.Victims}",
                Grave
            ),
            new(
                ClipKind.BStep,
                1,
                f => f.VictimCount > 0,
                f => $"Imagine bstepping over {f.Victims}. {f.Hero} did",
                Grave
            ),
            new(
                ClipKind.BStep,
                1,
                f => f.VictimCount > 0,
                f => $"GG no re? {f.Hero} bsteps over {f.Victims}",
                Grave
            ),
            new(ClipKind.BStep, 2, f => f.DiedAfter, f => $"{f.Hero} bsteps... then dies", Karma),
            new(
                ClipKind.BStep,
                2,
                f => f.DiedAfter,
                f => $"Instant karma: {f.Hero} bsteps and gets caught",
                Karma
            ),
            new(
                ClipKind.BStep,
                2,
                f => f.DiedAfter,
                f => $"Bstep BM backfires on {f.Hero}",
                Karma
            ),
            new(
                ClipKind.BStep,
                3,
                f => f.AllyBefore,
                f => $"Ally down, {f.Hero} bsteps anyway",
                Oops
            ),
            new(
                ClipKind.BStep,
                3,
                f => f.AllyBefore,
                f => $"{f.Hero} bsteps while a teammate falls?!",
                Oops
            ),
            new(ClipKind.BStep, 5, Always, f => $"{f.Hero} bstep BM", Grave),
            new(ClipKind.BStep, 5, Always, f => $"{f.Hero} breaks out the bstep", Grave),
            new(
                ClipKind.BStep,
                4,
                f => f.Cycles >= 8,
                f => $"{f.Hero} bsteps x{f.Cycles} in {f.BStepSeconds}s!?!?",
                Grave
            ),
            new(ClipKind.BStep, -1, f => f.IsHero("Murky"), f => "Mrrgl?! Murky bstep BM", Grave),
            // Pentakill: the announcer calls five a Mega Kill.
            new(
                ClipKind.Penta,
                0,
                f => f.Wiped,
                f => $"{f.Hero} PENTAKILL, whole team wiped",
                Fire
            ),
            new(
                ClipKind.Penta,
                0,
                f => f.Wiped,
                f => $"{f.Hero} wipes the whole team: pentakill",
                Fire
            ),
            new(
                ClipKind.Penta,
                0,
                f => f.Wiped,
                f => $"MEGA KILL! {f.Hero} pentakill team wipe",
                Fire
            ),
            new(ClipKind.Penta, 1, Always, f => $"Five for {f.Hero}: pentakill", Fire),
            new(ClipKind.Penta, 1, Always, f => $"{f.Hero} PENTAKILL", Fire),
            new(ClipKind.Penta, 1, Always, f => $"MEGA KILL! {f.Hero} pentakill", Fire),
            new(
                ClipKind.Penta,
                -1,
                f => f.Seconds >= 2 && f.Seconds <= 10,
                f => $"{f.Seconds}-second pentakill by {f.Hero}!",
                Fire
            ),
            // Quadra kill: one blow short of a pentakill.
            new(ClipKind.Quadra, 0, Always, f => $"{f.Hero} quadra kill, one short of a penta"),
            new(ClipKind.Quadra, 0, Always, f => $"So close... {f.Hero} quadra kill"),
            new(ClipKind.Quadra, 0, Always, f => $"Four down: {f.Hero} quadra kill"),
            new(ClipKind.Quadra, 0, Always, f => $"{f.Hero} quadra kill, the penta got away"),
            new(
                ClipKind.Quadra,
                -1,
                f => f.Seconds >= 2 && f.Seconds <= 8,
                f => $"{f.Hero} quadra kill in {f.Seconds} seconds"
            ),
            // Triple kill: three blows within a few seconds.
            new(ClipKind.Triple, 0, Always, f => $"Three down: {f.Hero} triple kill"),
            new(ClipKind.Triple, 0, Always, f => $"Triple kill for {f.Hero}"),
            new(ClipKind.Triple, 0, Always, f => $"{f.Hero} takes three: triple kill"),
            new(
                ClipKind.Triple,
                -1,
                f => f.Seconds <= 1,
                f => $"Blink and it's over: {f.Hero} triple kill"
            ),
            // Boss steal: the strongest fact of the fight tells the story.
            new(
                ClipKind.Steal,
                0,
                f => f.LevelsBehind >= 2,
                f => $"Down {f.LevelsBehind} levels, {f.Hero} steals the boss"
            ),
            new(
                ClipKind.Steal,
                0,
                f => f.LevelsBehind >= 2,
                f => $"{f.LevelsBehind} levels behind and {f.Hero} still steals the boss"
            ),
            new(
                ClipKind.Steal,
                1,
                f => f.KilledByOther && f.Other != null,
                f => $"{f.Other} kills the boss, {f.Hero} takes it"
            ),
            new(
                ClipKind.Steal,
                1,
                f => f.KilledByOther && f.Other != null,
                f => $"Yoink! {f.Hero} steals the boss from {f.Other}"
            ),
            new(
                ClipKind.Steal,
                2,
                f => f.StarterMost >= 3 && f.StarterMost >= f.CapturerMost + 2,
                f => $"{f.StarterMost} on the boss, {f.Hero} steals it anyway"
            ),
            new(
                ClipKind.Steal,
                3,
                f => f.StarterGroupSeconds >= 25 && f.Other != null,
                f => $"{f.Other} worked it {f.StarterGroupSeconds}s, {f.Hero} took the boss"
            ),
            new(
                ClipKind.Steal,
                3,
                f => f.StarterGroupSeconds >= 25,
                f => $"{f.StarterGroupSeconds}s of boss work, stolen by {f.Hero}"
            ),
            new(
                ClipKind.Steal,
                4,
                f => f.StarterHalf && f.Other != null,
                f => $"{f.Hero} steals the boss in {f.Other}'s half"
            ),
            new(ClipKind.Steal, 5, Always, f => $"{f.Hero} boss steal"),
            new(ClipKind.Steal, 5, Always, f => $"Yoink! {f.Hero} steals the boss"),
            new(ClipKind.Steal, 5, Always, f => $"Thanks for the boss: {f.Hero} steals it"),
            // Boss defended: the team that started it kept it.
            new(
                ClipKind.Defended,
                0,
                f => f.KilledByOther && f.Team != null,
                f => $"Steal denied! {f.Team} keeps the boss"
            ),
            new(
                ClipKind.Defended,
                0,
                f => f.KilledByOther && f.Team != null && f.Other != null,
                f => $"{f.Other} tries to steal, {f.Team} keeps the boss"
            ),
            new(
                ClipKind.Defended,
                1,
                f => f.PitDeaths >= 3 && f.TogetherSeconds >= 5 && f.Team != null,
                f =>
                    $"Boss held: {f.Team} wins a {f.TogetherSeconds}s pit fight, {f.PitDeaths} down"
            ),
            new(
                ClipKind.Defended,
                1,
                f => f.PitDeaths >= 3,
                f => $"{f.PitDeaths} heroes down, {f.Hero} secures the boss"
            ),
            new(ClipKind.Defended, 2, Always, f => $"{f.Hero} secures the boss"),
            new(ClipKind.Defended, 2, Always, f => $"Boss fight won: {f.Hero} secures it"),
        };

        // Heroes the community has a running joke about.
        lines.AddRange(HeroKillLines(new[] { "Chromie" }, f => $"Chromie hits her skillshots: {f.KindWord}"));
        lines.AddRange(HeroKillLines(new[] { "Raynor" }, f => $"Beginner hero? Raynor {f.KindWord}"));
        lines.AddRange(HeroKillLines(new[] { "Azmodan" }, f => $"Azmodunk! Azmodan {f.KindWord}"));
        lines.AddRange(HeroKillLines(new[] { "Abathur" }, f => $"Slug life: Abathur {f.KindWord}"));
        lines.AddRange(
            HeroKillLines(new[] { "Cho", "Gall" }, f => $"Two heads, one {f.KindWord}: {f.Hero}")
        );
        lines.AddRange(HeroKillLines(new[] { "Murky" }, f => $"Mrrgl! Murky {f.KindWord}"));
        lines.AddRange(HeroKillLines(new[] { "Illidan" }, f => $"Illidan was prepared: {f.KindWord}"));
        lines.AddRange(HeroKillLines(new[] { "The Butcher", "Butcher" }, f => $"Fresh meat! Butcher {f.KindWord}"));
        return lines;
    }
}
