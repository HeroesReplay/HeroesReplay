using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Observer;

public sealed class GameTimerLog
{
    private readonly ILogger<GameTimerLog> logger;
    private string lastSource = "";
    private string lastReason = "";
    private TimeSpan? lastTimer;

    public GameTimerLog(ILogger<GameTimerLog> logger)
    {
        this.logger = logger;
    }

    public void Write(Activity session, GameTimerReading reading)
    {
        bool warning =
            reading.Reason
            is "read-failed"
                or "unsupported-build"
                or "open-failed"
                or "bad-scale"
                or "implausible-jump"
                or "memory-unplayable";
        bool changed =
            reading.Source != lastSource
            || reading.Reason != lastReason
            || (reading.Time.HasValue && Moved(reading.Time.Value, lastTimer));
        if (!changed)
        {
            return;
        }

        if (warning)
        {
            logger.LogWarning(
                "Match clock fallback {ClockSource} reason {ClockReason} ticks {ClockTicks} scale {ClockScale} candidate {Timer}",
                reading.Source,
                reading.Reason,
                reading.Ticks,
                reading.Scale,
                reading.Time
            );
        }
        else if (reading.Ok)
        {
            logger.LogInformation(
                "Match clock {ClockSource} {Timer} reason {ClockReason} ticks {ClockTicks} scale {ClockScale}",
                reading.Source,
                reading.Time,
                reading.Reason,
                reading.Ticks,
                reading.Scale
            );
        }

        lastSource = reading.Source;
        lastReason = reading.Reason;
        if (reading.Time.HasValue)
        {
            lastTimer = reading.Time.Value;
        }

        if (session == null)
        {
            return;
        }

        session.SetTag("clock.source", reading.Source);
        session.SetTag("clock.reason", reading.Reason);
        session.SetTag("clock.ticks", reading.Ticks);
        session.SetTag("clock.scale", reading.Scale);
        if (reading.Time.HasValue)
        {
            session.SetTag("clock.seconds", reading.Time.Value.TotalSeconds);
        }

        session.AddEvent(
            new ActivityEvent(
                warning ? "clock.fallback" : "clock.memory",
                tags: new ActivityTagsCollection
                {
                    { "clock.source", reading.Source },
                    { "clock.reason", reading.Reason },
                    { "clock.ticks", reading.Ticks },
                    { "clock.scale", reading.Scale },
                }
            )
        );
    }

    /// <summary>
    /// TimeSpan.MinValue minus a match clock overflows. The first reading has no previous clock.
    /// </summary>
    internal static bool Moved(TimeSpan current, TimeSpan? previous)
    {
        if (previous == null)
        {
            return true;
        }

        try
        {
            return (current - previous.Value).Duration() >= TimeSpan.FromSeconds(1);
        }
        catch (OverflowException)
        {
            return true;
        }
    }
}
