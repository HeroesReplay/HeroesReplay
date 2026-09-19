using System;
using Heroes.ReplayParser;

namespace HeroesReplay.Core.Extensions;

public static class UnitLifetime
{
    public static bool IsAliveAt(this Unit unit, TimeSpan now)
    {
        if (unit == null)
        {
            return false;
        }

        if (unit.TimeSpanBorn >= now)
        {
            return false;
        }

        return !unit.TimeSpanDied.HasValue || unit.TimeSpanDied.Value > now;
    }

    public static int FloorSeconds(this TimeSpan time)
    {
        int seconds = (int)Math.Floor(time.TotalSeconds);
        return seconds < 0 ? 0 : seconds;
    }
}
