using System;

namespace HeroesReplay.Core.Spectating.Clock.Memory;

public readonly record struct ClockTelemetryReport(string State, string Reason);

/// <summary>
/// Pattern discovery finds the clock. Until a read is locked and ok, there is no match clock.
/// The same state and reason are reported once, not on every poll.
/// </summary>
public static class ClockTelemetry
{
    public const string Discovering = "discovering";
    public const string MemoryLocked = "memory-locked";
    public const string Unlocked = "memory-unlocked";

    public static ClockTelemetryReport Describe(bool discovered, bool located, string reason)
    {
        string detail = reason ?? "";
        if (!discovered)
        {
            return new ClockTelemetryReport(Discovering, detail);
        }

        if (located && string.Equals(detail, "ok", StringComparison.Ordinal))
        {
            return new ClockTelemetryReport(MemoryLocked, detail);
        }

        return new ClockTelemetryReport(Unlocked, detail);
    }

    public static bool Changed(ClockTelemetryReport previous, ClockTelemetryReport next)
    {
        return !string.Equals(previous.State, next.State, StringComparison.Ordinal)
            || !string.Equals(previous.Reason, next.Reason, StringComparison.Ordinal);
    }
}
