using System;
using HeroesReplay.Core.Spectating.Screens;

namespace HeroesReplay.Core.Spectating.Session;

/// <summary>
/// The report stays up while the next client is starting and the map is not on screen.
/// Map loading, the countdown before gates, or a running match clock ends the report. A Blind
/// Rating replay's loading screen names the players, so only its match clock ends the report
/// (<see cref="ReplayLoadCue.LoadingScreenOnAir"/>, #411).
/// </summary>
public static class ReportHandoff
{
    public static bool ShouldCutReport(bool mapLoading, TimeSpan? matchClock, bool blind = false) =>
        matchClock.HasValue || (mapLoading && ReplayLoadCue.LoadingScreenOnAir(blind));
}
