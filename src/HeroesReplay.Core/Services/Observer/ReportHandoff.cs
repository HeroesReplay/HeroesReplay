using System;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// The report stays up while the next client is starting and the map is not on screen.
/// Map loading, the countdown before gates, or a running match clock ends the report.
/// </summary>
public static class ReportHandoff
{
    public static bool ShouldCutReport(bool mapLoading, TimeSpan? matchClock) =>
        mapLoading || matchClock.HasValue;
}
