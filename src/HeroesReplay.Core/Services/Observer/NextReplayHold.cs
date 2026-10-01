using System;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// How long to leave the report scenes up before the next Heroes client launches.
/// </summary>
public static class NextReplayHold
{
    public static readonly TimeSpan Default = TimeSpan.FromMinutes(1);

    public static TimeSpan Duration(TimeSpan configured)
    {
        if (configured < TimeSpan.Zero)
        {
            return Default;
        }

        return configured;
    }

    /// <summary>
    /// A positive hold finishes the report cycle before launch, so a completed report
    /// is not a reason to stop watching for the next loading screen.
    /// </summary>
    public static bool StopWhenReportEnds(TimeSpan hold) => hold <= TimeSpan.Zero;
}
