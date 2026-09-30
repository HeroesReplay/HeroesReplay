using System;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// The report stays up while the next client is loading or still on a draft countdown.
/// A match clock at zero or later ends the report so spectating starts at that clock.
/// </summary>
public static class ReportHandoff
{
    public static bool ShouldCutReport(TimeSpan? matchClock) =>
        matchClock.HasValue && matchClock.Value >= TimeSpan.Zero;
}
