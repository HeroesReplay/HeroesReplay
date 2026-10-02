using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.Spectating;

public static class TalentPanelSchedule
{
    public static bool ShouldShow(
        IEnumerable<TimeSpan> talentTimes,
        TimeSpan now,
        TimeSpan hold,
        TimeSpan cluster
    )
    {
        if (talentTimes == null || hold <= TimeSpan.Zero)
        {
            return false;
        }

        TimeSpan? start = null;
        TimeSpan end = TimeSpan.Zero;
        foreach (TimeSpan time in talentTimes.OrderBy(t => t))
        {
            if (start == null)
            {
                start = time;
                end = time + hold;
                continue;
            }

            if (time <= end + cluster)
            {
                end = time + hold;
                continue;
            }

            if (now >= start && now <= end)
            {
                return true;
            }

            start = time;
            end = time + hold;
        }

        return start != null && now >= start.Value && now <= end;
    }
}
