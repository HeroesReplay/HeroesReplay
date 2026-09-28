using System;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

public static class ReplayInfoVisibility
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(45);

    public static bool ShouldShow(TimeSpan matchTime, TimeSpan visibleFor)
    {
        if (visibleFor <= TimeSpan.Zero)
        {
            visibleFor = Default;
        }

        return matchTime < visibleFor;
    }
}
