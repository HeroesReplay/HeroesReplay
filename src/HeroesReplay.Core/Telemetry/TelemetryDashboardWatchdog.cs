using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.Core.Telemetry;

/// <summary>What <see cref="TelemetryDashboardState.State"/> says.</summary>
public static class TelemetryDashboardStates
{
    /// <summary><c>OpenTelemetry:Enabled</c> or <c>OpenTelemetry:Dashboard:Supervise</c> is false.</summary>
    public const string Off = "off";

    /// <summary>OTLP has not been probed yet.</summary>
    public const string Unknown = "unknown";

    /// <summary>OTLP gRPC answers: the roles' telemetry is landing.</summary>
    public const string Up = "up";

    /// <summary>OTLP does not answer. It is relaunched after its backoff.</summary>
    public const string Down = "down";

    /// <summary>A relaunch is under way, off the supervisor's thread.</summary>
    public const string Restarting = "restarting";
}

/// <summary>
/// The dashboard part of <c>supervisor.json</c> (#538). <c>services status</c> prints it, so a
/// dashboard that died shows as <c>down</c> instead of nothing.
/// </summary>
public sealed class TelemetryDashboardState
{
    public const string DownCode = "telemetry.dashboard_down";

    public string State { get; set; } = TelemetryDashboardStates.Unknown;
    public string Reason { get; set; }
    public DateTimeOffset? CheckedAt { get; set; }
    public DateTimeOffset? UpSince { get; set; }
    public DateTimeOffset? DownSince { get; set; }

    /// <summary>Relaunches this supervisor started, whatever came of them.</summary>
    public int Restarts { get; set; }

    /// <summary>Relaunches in a row that did not bring OTLP back. Reset when it answers.</summary>
    public int FailedRestarts { get; set; }
    public DateTimeOffset? LastRestartAt { get; set; }
    public DateTimeOffset? NextRestartAt { get; set; }
    public string LastFailure { get; set; }

    /// <summary>True when the dashboard should be up and is not.</summary>
    public bool Degraded =>
        State is TelemetryDashboardStates.Down or TelemetryDashboardStates.Restarting;

    public string Describe(DateTimeOffset now)
    {
        switch (State)
        {
            case TelemetryDashboardStates.Off:
            case TelemetryDashboardStates.Unknown:
                return string.IsNullOrWhiteSpace(Reason) ? $"{State}." : $"{State}. {Reason}";
            case TelemetryDashboardStates.Up:
                return $"up{Since(UpSince, now)}; relaunches {Restarts}.";
        }

        var facts = new List<string> { $"{State} [{DownCode}]{Since(DownSince, now)}" };
        if (NextRestartAt is DateTimeOffset next && State == TelemetryDashboardStates.Down)
        {
            facts.Add($"next relaunch in {Span(next - now)}");
        }

        facts.Add($"relaunches {Restarts}");
        if (!string.IsNullOrWhiteSpace(LastFailure))
        {
            facts.Add("last: " + LastFailure);
        }

        return string.Join("; ", facts) + ". Traces, logs, and metrics are dropped while it is down.";
    }

    private static string Since(DateTimeOffset? at, DateTimeOffset now) =>
        at is DateTimeOffset value ? $" for {Span(now - value)}" : string.Empty;

    public static string Span(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalSeconds < 60)
        {
            return $"{(int)span.TotalSeconds}s";
        }

        if (span.TotalMinutes < 60)
        {
            return $"{(int)span.TotalMinutes}m";
        }

        if (span.TotalHours < 48)
        {
            return span.Minutes == 0
                ? $"{(int)span.TotalHours}h"
                : $"{(int)span.TotalHours}h {span.Minutes}m";
        }

        return $"{(int)span.TotalDays}d {span.Hours}h";
    }
}

/// <summary>
/// Keeps the local Aspire dashboard up for the supervisor (#538). Before this the dashboard was
/// started once by <c>services start</c>; when it died, every role exported into a closed port for
/// days and <c>services status</c> still said ok. Each pass after <c>CheckInterval</c> probes OTLP;
/// when it is gone the dashboard is relaunched off the supervisor's thread, then again after each
/// <c>RestartDelays</c> wait while it stays down. It never touches the roles, OBS, or the stream.
/// </summary>
public sealed class TelemetryDashboardWatchdog
{
    private Task<string> relaunch;
    private DateTimeOffset? nextCheckAt;

    public TelemetryDashboardWatchdog(TelemetryDashboardSettings settings, bool telemetryEnabled)
    {
        Settings = settings ?? new TelemetryDashboardSettings();
        Enabled = telemetryEnabled && Settings.Supervise;
        if (!Enabled)
        {
            State.State = TelemetryDashboardStates.Off;
            State.Reason = telemetryEnabled
                ? "OpenTelemetry:Dashboard:Supervise is false."
                : "OpenTelemetry:Enabled is false.";
        }
    }

    public TelemetryDashboardSettings Settings { get; }
    public bool Enabled { get; }
    public TelemetryDashboardState State { get; } = new();

    /// <summary>True when OTLP gRPC answers.</summary>
    public Func<bool> Listening { get; init; }

    /// <summary>
    /// Starts the dashboard and waits for OTLP. Returns null when it is up, or why it is not.
    /// Runs off the supervisor's thread.
    /// </summary>
    public Func<string> Relaunch { get; init; }

    /// <summary>Runs <see cref="Relaunch"/>. Tests pass one that runs inline.</summary>
    public Func<Func<string>, Task<string>> Run { get; init; } = work => Task.Run(work);

    public ILogger Logger { get; init; } = NullLogger.Instance;

    /// <summary>One pass. True when <see cref="State"/> changed, so <c>supervisor.json</c> is saved.</summary>
    public bool Tick(DateTimeOffset now)
    {
        if (!Enabled || Listening == null)
        {
            return false;
        }

        if (relaunch != null)
        {
            if (!relaunch.IsCompleted)
            {
                return false;
            }

            return Finish(now);
        }

        if (nextCheckAt is DateTimeOffset due && now < due)
        {
            return false;
        }

        nextCheckAt = now + Settings.EffectiveCheckInterval;
        bool listening = Probe();
        State.CheckedAt = now;
        if (listening)
        {
            return MarkUp(now);
        }

        bool changed = false;
        if (State.State != TelemetryDashboardStates.Down)
        {
            bool wasUp = State.State == TelemetryDashboardStates.Up;
            State.State = TelemetryDashboardStates.Down;
            State.DownSince = now;
            State.UpSince = null;
            State.NextRestartAt = now;
            changed = true;
            Logger.LogWarning(
                "Telemetry dashboard is down [{Code}]: OTLP {Endpoint} does not answer{Was}. Traces, logs, and metrics are dropped until it is back. The supervisor relaunches it now; the roles, OBS, and the stream are not touched.",
                TelemetryDashboardState.DownCode,
                "gRPC",
                wasUp ? " (it was up)" : string.Empty
            );
        }

        if (State.NextRestartAt is DateTimeOffset next && now < next)
        {
            return changed;
        }

        Start(now);
        return true;
    }

    private bool Probe()
    {
        try
        {
            return Listening();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool MarkUp(DateTimeOffset now)
    {
        if (State.State == TelemetryDashboardStates.Up)
        {
            return false;
        }

        if (State.State is TelemetryDashboardStates.Down or TelemetryDashboardStates.Restarting)
        {
            Logger.LogInformation(
                "Telemetry dashboard is back after {Down} ({Restarts} relaunches).",
                TelemetryDashboardState.Span(now - (State.DownSince ?? now)),
                State.Restarts
            );
        }

        State.State = TelemetryDashboardStates.Up;
        State.UpSince = now;
        State.DownSince = null;
        State.NextRestartAt = null;
        State.FailedRestarts = 0;
        State.Reason = null;
        return true;
    }

    private void Start(DateTimeOffset now)
    {
        State.State = TelemetryDashboardStates.Restarting;
        State.Restarts++;
        State.LastRestartAt = now;
        State.NextRestartAt = null;
        Func<string> work = Relaunch ?? (() => "no relaunch is configured");
        try
        {
            relaunch = Run(() =>
            {
                try
                {
                    return work();
                }
                catch (Exception e)
                {
                    return e.Message;
                }
            });
        }
        catch (Exception e)
        {
            relaunch = Task.FromResult(e.Message);
        }
    }

    private bool Finish(DateTimeOffset now)
    {
        string failure;
        try
        {
            failure = relaunch.Result;
        }
        catch (Exception e)
        {
            failure = e.GetBaseException().Message;
        }

        relaunch = null;
        nextCheckAt = now + Settings.EffectiveCheckInterval;
        State.CheckedAt = now;
        if (failure == null && Probe())
        {
            return MarkUp(now);
        }

        State.FailedRestarts++;
        State.LastFailure = string.IsNullOrWhiteSpace(failure)
            ? "relaunched, but OTLP does not answer yet"
            : failure;
        TimeSpan delay = Settings.DelayFor(State.FailedRestarts);
        State.State = TelemetryDashboardStates.Down;
        State.NextRestartAt = now + delay;
        Logger.LogWarning(
            "Telemetry dashboard relaunch {Attempt} did not bring OTLP back [{Code}]: {Failure} Next relaunch in {Delay}.",
            State.Restarts,
            TelemetryDashboardState.DownCode,
            State.LastFailure,
            TelemetryDashboardState.Span(delay)
        );
        return true;
    }
}
