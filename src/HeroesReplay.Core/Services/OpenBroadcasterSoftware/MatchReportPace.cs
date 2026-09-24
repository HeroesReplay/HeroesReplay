using System;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

public static class MatchReportPace
{
    /// <summary>Pixels per second that finish the 7200px report page in three minutes.</summary>
    public const double ReferenceSpeedY = 34;

    public static readonly TimeSpan ReferenceDuration = TimeSpan.FromMinutes(3);

    public static double ScrollSpeedY(TimeSpan displayTime)
    {
        if (displayTime <= TimeSpan.Zero)
        {
            return ReferenceSpeedY;
        }

        return ReferenceSpeedY * ReferenceDuration.TotalSeconds / displayTime.TotalSeconds;
    }
}
