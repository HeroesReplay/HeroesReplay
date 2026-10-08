using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.YouTube.Metadata;

public enum StatHookKind
{
    Counter,
    Duo,
    BestMap,
    SlipsTheBan,
    WorstMap,
    Powerhouse,
    Underdog,
}

/// <summary>
/// One title hook: the words for the title (never a number) and the <c>Stats:</c> description
/// line that carries its numbers.
/// </summary>
public sealed record StatHook(StatHookKind Kind, string Title, string StatsLine);

/// <summary>
/// Picks at most one title hook from Heroes Profile hero statistics (issue #272). In priority
/// order: <c>A counters B</c>, <c>A + B duo</c>, <c>Hero's best map</c>, <c>Hero slips the ban</c>,
/// <c>Hero's worst map</c>, then <c>Patch powerhouse Hero</c> or <c>Underdog Hero</c>. The first
/// kind with a match that clears its thresholds wins; within a kind the strongest match wins. A
/// hero the title already names (<c>X focus</c>, <c>Ft. X</c>) is skipped, and so is a pair that
/// includes it. Heroes are matched to the statistics by heroes-data2 <c>AttributeId</c>.
/// </summary>
public static class StatHookPicker
{
    public const string Attribution =
        "Data provided by Heroes Profile: https://www.heroesprofile.com/";

    public const string StatsLabel = "Stats:";

    public static StatHook Pick(
        HeroStatsSnapshot stats,
        IReadOnlyList<Hero> catalog,
        IReadOnlyList<ReplayMediaPlayer> roster,
        string map,
        IReadOnlyCollection<string> namedHeroes,
        StatHookSettings settings,
        string gameMode = null
    )
    {
        if (stats?.Heroes == null || roster == null || roster.Count == 0 || settings == null)
        {
            return null;
        }

        List<Entry> entries = Entries(stats, catalog, roster, namedHeroes);
        if (entries.Count == 0)
        {
            return null;
        }

        string context = Context(stats, gameMode);
        StatHook hook =
            (settings.Counters ? Counter(entries, settings, context) : null)
            ?? (settings.Duos ? Duo(entries, settings, context) : null)
            ?? (settings.BestMap ? MapEdge(entries, map, settings, context, best: true) : null)
            ?? (settings.SlipsTheBan ? Ban(entries, map, settings, context) : null)
            ?? (settings.WorstMap ? MapEdge(entries, map, settings, context, best: false) : null)
            ?? (settings.PatchExtremes ? Extreme(stats, entries, settings, context) : null);
        if (hook == null || string.IsNullOrWhiteSpace(hook.Title))
        {
            return null;
        }

        // TryReadTitleId reads the first all-digit title part as the replay id.
        return int.TryParse(hook.Title.Trim(), out _) ? null : hook;
    }

    private static StatHook Counter(List<Entry> entries, StatHookSettings settings, string context)
    {
        Entry bestA = null;
        Entry bestB = null;
        HeroPairStats bestPair = null;
        double bestEdge = double.MinValue;
        double bestExpected = 0;
        foreach (Entry a in entries.Where(entry => !entry.Named))
        {
            foreach (Entry b in entries.Where(entry => !entry.Named && entry.Team != a.Team))
            {
                HeroPairStats pair = a.Stats.Enemy(b.Stats.AttributeId);
                if (
                    pair == null
                    || pair.Games < settings.CounterMinGames
                    || pair.WinRate is not double rate
                    || rate < settings.CounterMinWinRate
                    || a.Stats.WinRate is not double baseA
                    || b.Stats.WinRate is not double baseB
                )
                {
                    continue;
                }

                double expected = HeroStatsMath.ExpectedAgainst(baseA, baseB);
                double lower = HeroStatsMath.WilsonLower(pair.Wins, pair.Games);
                double edge = rate - expected;
                if (lower <= 50 || lower <= expected || edge < settings.CounterMinEdge)
                {
                    continue;
                }

                if (edge > bestEdge || (edge == bestEdge && pair.Games > bestPair.Games))
                {
                    (bestA, bestB, bestPair, bestEdge, bestExpected) = (a, b, pair, edge, expected);
                }
            }
        }

        if (bestPair == null)
        {
            return null;
        }

        return new StatHook(
            StatHookKind.Counter,
            bestA.Name + " counters " + bestB.Name,
            Line(
                $"{bestA.Name} won {Percent(bestPair.WinRate)} of {bestPair.Games} games against {bestB.Name}, {Points(bestEdge)} points over the {Percent(bestExpected)} their overall win rates predict",
                context
            )
        );
    }

    private static StatHook Duo(List<Entry> entries, StatHookSettings settings, string context)
    {
        Entry bestA = null;
        Entry bestB = null;
        HeroPairStats bestPair = null;
        for (int i = 0; i < entries.Count; i++)
        {
            Entry a = entries[i];
            if (a.Named)
            {
                continue;
            }

            for (int j = i + 1; j < entries.Count; j++)
            {
                Entry b = entries[j];
                if (b.Named || b.Team != a.Team)
                {
                    continue;
                }

                HeroPairStats pair =
                    a.Stats.Ally(b.Stats.AttributeId) ?? b.Stats.Ally(a.Stats.AttributeId);
                if (
                    pair == null
                    || pair.Games < settings.DuoMinGames
                    || pair.WinRate is not double rate
                    || rate < settings.DuoMinWinRate
                    || HeroStatsMath.WilsonLower(pair.Wins, pair.Games) < settings.DuoMinLowerBound
                )
                {
                    continue;
                }

                if (
                    bestPair == null
                    || rate > bestPair.WinRate
                    || (rate == bestPair.WinRate && pair.Games > bestPair.Games)
                )
                {
                    (bestA, bestB, bestPair) = (a, b, pair);
                }
            }
        }

        if (bestPair == null)
        {
            return null;
        }

        return new StatHook(
            StatHookKind.Duo,
            bestA.Name + " + " + bestB.Name + " duo",
            Line(
                $"{bestA.Name} and {bestB.Name} won {Percent(bestPair.WinRate)} of {bestPair.Games} games together",
                context
            )
        );
    }

    /// <summary>
    /// Best map: this map is the hero's highest win rate among maps with enough games, at least
    /// <c>MapMinDelta</c> points over its overall rate. Worst map is the reverse.
    /// </summary>
    private static StatHook MapEdge(
        List<Entry> entries,
        string map,
        StatHookSettings settings,
        string context,
        bool best
    )
    {
        if (string.IsNullOrWhiteSpace(map))
        {
            return null;
        }

        Entry chosen = null;
        HeroMapStats chosenRow = null;
        double chosenDelta = 0;
        foreach (Entry entry in entries.Where(entry => !entry.Named))
        {
            if (entry.Stats.WinRate is not double overall)
            {
                continue;
            }

            List<HeroMapStats> eligible = (entry.Stats.Maps ?? new List<HeroMapStats>())
                .Where(row =>
                    row != null && row.Games >= settings.MapMinGames && row.WinRate != null
                )
                .ToList();
            HeroMapStats here = eligible.FirstOrDefault(row => SameMap(row.Map, map));
            if (here == null || eligible.Count < 2)
            {
                continue;
            }

            double rate = here.WinRate.Value;
            bool extreme = best
                ? eligible.All(row => row == here || row.WinRate < rate)
                : eligible.All(row => row == here || row.WinRate > rate);
            double delta = best ? rate - overall : overall - rate;
            if (!extreme || delta < settings.MapMinDelta)
            {
                continue;
            }

            if (chosen == null || delta > chosenDelta)
            {
                (chosen, chosenRow, chosenDelta) = (entry, here, delta);
            }
        }

        if (chosen == null)
        {
            return null;
        }

        string which = best ? "best" : "worst";
        string direction = best ? "over" : "under";
        return new StatHook(
            best ? StatHookKind.BestMap : StatHookKind.WorstMap,
            chosen.Name + "'s " + which + " map",
            Line(
                $"{chosen.Name} won {Percent(chosenRow.WinRate)} of {chosenRow.Games} games on {chosenRow.Map}, the hero's {which} map, {Points(chosenDelta)} points {direction} {Percent(chosen.Stats.WinRate)} overall",
                context
            )
        );
    }

    private static StatHook Ban(
        List<Entry> entries,
        string map,
        StatHookSettings settings,
        string context
    )
    {
        if (string.IsNullOrWhiteSpace(map))
        {
            return null;
        }

        Entry chosen = null;
        HeroMapStats chosenRow = null;
        foreach (Entry entry in entries.Where(entry => !entry.Named))
        {
            HeroMapStats here = (entry.Stats.Maps ?? new List<HeroMapStats>()).FirstOrDefault(row =>
                row != null && SameMap(row.Map, map)
            );
            if (
                here == null
                || here.Games < settings.BanMinGames
                || here.BanRate < settings.BanMinRate
            )
            {
                continue;
            }

            if (chosenRow == null || here.BanRate > chosenRow.BanRate)
            {
                (chosen, chosenRow) = (entry, here);
            }
        }

        if (chosen == null)
        {
            return null;
        }

        return new StatHook(
            StatHookKind.SlipsTheBan,
            chosen.Name + " slips the ban",
            Line(
                $"{chosen.Name} is banned in {Percent(chosenRow.BanRate)} of games on {chosenRow.Map}",
                context
            )
        );
    }

    /// <summary>A hero among the patch's <c>ExtremesCount</c> highest or lowest win rates.</summary>
    private static StatHook Extreme(
        HeroStatsSnapshot stats,
        List<Entry> entries,
        StatHookSettings settings,
        string context
    )
    {
        int count = settings.ExtremesCount;
        List<HeroStats> ranked = stats
            .Heroes.Where(hero =>
                hero != null && hero.Games >= settings.ExtremesMinGames && hero.WinRate != null
            )
            .OrderByDescending(hero => hero.WinRate)
            .ToList();
        if (count <= 0 || ranked.Count <= 2 * count)
        {
            return null;
        }

        HashSet<HeroStats> top = ranked.Take(count).ToHashSet();
        HashSet<HeroStats> bottom = ranked.Skip(ranked.Count - count).ToHashSet();
        Entry chosen = null;
        bool chosenTop = false;
        double chosenDistance = double.MinValue;
        foreach (Entry entry in entries.Where(entry => !entry.Named))
        {
            bool isTop = top.Contains(entry.Stats);
            if (!isTop && !bottom.Contains(entry.Stats))
            {
                continue;
            }

            double distance = Math.Abs(entry.Stats.WinRate.Value - 50);
            if (distance > chosenDistance)
            {
                (chosen, chosenTop, chosenDistance) = (entry, isTop, distance);
            }
        }

        if (chosen == null)
        {
            return null;
        }

        string end = chosenTop ? "highest" : "lowest";
        return new StatHook(
            chosenTop ? StatHookKind.Powerhouse : StatHookKind.Underdog,
            (chosenTop ? "Patch powerhouse " : "Underdog ") + chosen.Name,
            Line(
                $"{chosen.Name} won {Percent(chosen.Stats.WinRate)} of {chosen.Stats.Games} games, one of the {count} {end} win rates of the patch",
                context
            )
        );
    }

    private static List<Entry> Entries(
        HeroStatsSnapshot stats,
        IReadOnlyList<Hero> catalog,
        IReadOnlyList<ReplayMediaPlayer> roster,
        IReadOnlyCollection<string> namedHeroes
    )
    {
        var entries = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ReplayMediaPlayer player in roster)
        {
            if (player == null || string.IsNullOrWhiteSpace(player.Hero))
            {
                continue;
            }

            string display = player.Hero.Trim();
            Hero hero = catalog == null ? null : HeroDraft.Find(catalog, display);
            HeroStats heroStats =
                stats.Find(hero?.AttributeId)
                ?? stats.Find(display)
                ?? stats.Heroes.FirstOrDefault(candidate =>
                    candidate != null && HeroDraft.SameName(candidate.Name, display)
                );
            if (heroStats == null || !seen.Add(player.Team + ":" + heroStats.AttributeId))
            {
                continue;
            }

            bool named =
                namedHeroes != null
                && namedHeroes.Any(name =>
                    !string.IsNullOrWhiteSpace(name)
                    && (
                        HeroDraft.SameName(name, display)
                        || HeroDraft.SameName(name, hero?.Name)
                        || HeroDraft.SameName(name, heroStats.Name)
                    )
                );
            entries.Add(new Entry(display, player.Team, heroStats, named));
        }

        return entries;
    }

    private static bool SameMap(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        string a = EnglishMapNames.Canonical(left) ?? left.Trim();
        string b = EnglishMapNames.Canonical(right) ?? right.Trim();
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static string Context(HeroStatsSnapshot stats, string gameMode)
    {
        string mode = string.IsNullOrWhiteSpace(gameMode)
            ? GameTypeLabel(stats.GameType)
            : gameMode.Trim();
        string patch = string.IsNullOrWhiteSpace(stats.Patch) ? null : "patch " + stats.Patch;
        return string.Join(
            ", ",
            new[] { mode, patch }.Where(part => !string.IsNullOrWhiteSpace(part))
        );
    }

    private static string GameTypeLabel(string code) =>
        HeroStatsPatch.GameTypeCode(code) switch
        {
            "sl" => "Storm League",
            "qm" => "Quick Match",
            "ar" => "ARAM",
            "ud" => "Unranked Draft",
            "hl" => "Hero League",
            "tl" => "Team League",
            _ => null,
        };

    private static string Line(string sentence, string context) =>
        StatsLabel
        + " "
        + sentence
        + (string.IsNullOrWhiteSpace(context) ? "." : " (" + context + ").");

    private static string Percent(double? rate) =>
        (rate ?? 0).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Points(double points) =>
        points.ToString("0.0", CultureInfo.InvariantCulture);

    private sealed record Entry(string Name, int Team, HeroStats Stats, bool Named);
}
