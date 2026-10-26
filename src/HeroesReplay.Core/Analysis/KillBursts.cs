using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Analysis;

/// <param name="Start">The index of the burst's first kill in the ordered seconds.</param>
/// <param name="Kills">How many kills the burst holds, at least the minimum.</param>
public readonly record struct KillBurst(int Start, int Kills, int StartSecond, int EndSecond);

/// <summary>
/// One hero's kills in a quick burst (#511): at least <c>minimumKills</c> within
/// <c>spanSeconds</c> of the first, such as a single big spell. Unlike
/// <see cref="KillStreaks"/>, which chains kills while each comes within a gap of the last, a
/// burst is bounded by its total span. Bursts never overlap: the next one starts after the last
/// kill of the one before. On 1,965 Storm League games, 3 hero killing blows within 3 s by one
/// hero happened in 55 games, and twice in one game in 2.
/// </summary>
public static class KillBursts
{
    public const int MinimumKills = 3;

    public const int SpanSeconds = 3;

    public static IReadOnlyList<KillBurst> Find(
        IReadOnlyList<int> orderedSeconds,
        int minimumKills = MinimumKills,
        int spanSeconds = SpanSeconds
    )
    {
        if (orderedSeconds == null || minimumKills < 2 || orderedSeconds.Count < minimumKills)
        {
            return Array.Empty<KillBurst>();
        }

        spanSeconds = Math.Max(0, spanSeconds);
        var bursts = new List<KillBurst>();
        int index = 0;
        while (index + minimumKills - 1 < orderedSeconds.Count)
        {
            int first = orderedSeconds[index];
            if (orderedSeconds[index + minimumKills - 1] - first > spanSeconds)
            {
                index++;
                continue;
            }

            int last = index + minimumKills - 1;
            while (
                last + 1 < orderedSeconds.Count && orderedSeconds[last + 1] - first <= spanSeconds
            )
            {
                last++;
            }

            bursts.Add(new KillBurst(index, last - index + 1, first, orderedSeconds[last]));
            index = last + 1;
        }

        return bursts;
    }
}
