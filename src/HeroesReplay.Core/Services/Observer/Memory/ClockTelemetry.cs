using System;

namespace HeroesReplay.Core.Services.Observer;

public readonly record struct ClockTelemetryReport(string State, string Reason);

/// <summary>
/// Pattern discovery is the default read. OCR is the fallback when that read is not locked.
/// The same state and reason are reported once, not on every poll.
/// </summary>
public static class ClockTelemetry
{
    public const string Discovering = "discovering";
    public const string MemoryLocked = "memory-locked";
    public const string OcrFallback = "ocr-fallback";

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

        return new ClockTelemetryReport(OcrFallback, detail);
    }

    public static bool Changed(ClockTelemetryReport previous, ClockTelemetryReport next)
    {
        return !string.Equals(previous.State, next.State, StringComparison.Ordinal)
            || !string.Equals(previous.Reason, next.Reason, StringComparison.Ordinal);
    }
}
