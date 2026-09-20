using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Services.Analysis;

public readonly record struct KillStreak(int StartSecond, int EndSecond, int Kills);

public static class KillStreaks
{
    public static IReadOnlyList<KillStreak> Group(
        IReadOnlyList<int> orderedSeconds,
        int windowSeconds
    )
    {
        if (orderedSeconds == null || orderedSeconds.Count == 0)
        {
            return Array.Empty<KillStreak>();
        }

        if (windowSeconds < 0)
        {
            windowSeconds = 0;
        }

        var streaks = new List<KillStreak>();
        int start = orderedSeconds[0];
        int end = orderedSeconds[0];
        int count = 1;

        for (int i = 1; i < orderedSeconds.Count; i++)
        {
            int second = orderedSeconds[i];
            if (second - end <= windowSeconds)
            {
                end = second;
                count++;
            }
            else
            {
                streaks.Add(new KillStreak(start, end, count));
                start = second;
                end = second;
                count = 1;
            }
        }

        streaks.Add(new KillStreak(start, end, count));
        return streaks;
    }
}
