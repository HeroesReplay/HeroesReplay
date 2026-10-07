using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>What the supervisor does with one role on one pass.</summary>
public enum ServiceRestartAction
{
    /// <summary>Nothing: the role is healthy, waiting out its backoff, or left down.</summary>
    None,

    /// <summary>The role just went down. A restart is due at <see cref="ServiceRoleRestarts.NextRestartAt"/>.</summary>
    Scheduled,

    /// <summary>The backoff is over and the budget has room. Restart it now.</summary>
    Restart,

    /// <summary>
    /// The process is alive but its heartbeat is older than the stale restart limit. Kill it; it
    /// then restarts like a failed role, against the same budget.
    /// </summary>
    Kill,

    /// <summary>The role went down with no budget left. It stays down; log one error.</summary>
    Exhausted,
}

/// <summary>
/// One role's restart history as the supervisor keeps it. Written to <c>supervisor.json</c> so
/// <c>services status</c> can show the restart count and the budget.
/// </summary>
public sealed class ServiceRoleRestarts
{
    public string Role { get; set; }

    /// <summary>The nonce of the record the supervisor last saw or started for this role.</summary>
    public string Nonce { get; set; }

    /// <summary>Restart attempts this supervisor made, including ones that did not get ready.</summary>
    public int Count { get; set; }

    /// <summary>Restart attempts inside the budget window, oldest first.</summary>
    public List<DateTimeOffset> Recent { get; set; } = new();
    public DateTimeOffset? LastRestartAt { get; set; }

    /// <summary><c>failed</c>, <c>stale</c>, or <c>launch_stalled</c>: why the last restart happened.</summary>
    public string LastReason { get; set; }

    /// <summary>Why the last attempt did not get ready. Null when it did.</summary>
    public string LastFailure { get; set; }

    /// <summary>When the supervisor saw the role down. Null while it runs.</summary>
    public DateTimeOffset? DownSince { get; set; }
    public string DownReason { get; set; }
    public string DownCause { get; set; }
    public DateTimeOffset? StaleSince { get; set; }
    public DateTimeOffset? NextRestartAt { get; set; }
    public bool Exhausted { get; set; }
    public DateTimeOffset? ExhaustedAt { get; set; }
}

/// <summary>
/// The restart rules, kept pure so a fake clock can drive them. A failed role restarts after the
/// backoff for the restarts already in the window (10 s, 30 s, 2 min, 5 min by default). A role
/// stale past <see cref="ServiceRestartSettings.StaleRestartAfter"/> is killed first. When the
/// window already holds the whole budget, the role stays down. Ready, degraded, and stopped roles
/// are left alone, except a spectate launch that stalled, which is killed like a stale role
/// while the budget has room. A stop request is the caller's to check before any restart.
/// </summary>
public static class ServiceRestartPolicy
{
    public const string FailedReason = "failed";
    public const string StaleReason = "stale";

    /// <summary>A spectate launch with no match progress past its threshold.</summary>
    public const string StalledReason = "launch_stalled";

    public static ServiceRestartAction Decide(
        ServiceRoleHealth health,
        ServiceRoleRestarts ledger,
        DateTimeOffset now,
        ServiceRestartSettings settings
    )
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(ledger);
        settings ??= new ServiceRestartSettings();
        Prune(ledger, now, settings);
        if (ledger.Exhausted)
        {
            return ServiceRestartAction.None;
        }

        switch (health.State)
        {
            case ServiceRoleState.Failed:
                ledger.StaleSince = null;
                if (ledger.DownSince == null)
                {
                    ledger.DownSince = now;
                    ledger.DownReason ??= FailedReason;
                    ledger.DownCause = health.Cause;
                    if (ledger.Recent.Count >= settings.Limit)
                    {
                        return Exhaust(ledger, now);
                    }

                    ledger.NextRestartAt = now + settings.BackoffFor(ledger.Recent.Count);
                    return ServiceRestartAction.Scheduled;
                }

                if (ledger.NextRestartAt is DateTimeOffset due && now < due)
                {
                    return ServiceRestartAction.None;
                }

                return ledger.Recent.Count >= settings.Limit
                    ? Exhaust(ledger, now)
                    : ServiceRestartAction.Restart;

            case ServiceRoleState.Stale:
                ledger.StaleSince ??= now;
                TimeSpan age = health.HeartbeatAgeSeconds is long seconds
                    ? TimeSpan.FromSeconds(seconds)
                    : now - ledger.StaleSince.Value;
                if (age < settings.StaleLimit)
                {
                    return ServiceRestartAction.None;
                }

                ledger.DownReason = StaleReason;
                ledger.DownCause = health.Cause;
                return ServiceRestartAction.Kill;

            // Degraded is left alone, except a spectate launch that stalled: it heartbeats, so
            // only this restarts it (#249). The kill makes it failed, and the failed role
            // restarts after its backoff against the same budget.
            case ServiceRoleState.Degraded
                when health.CauseCode == ServiceHealthCodes.SpectateLaunchStalled:
                ledger.StaleSince = null;
                if (ledger.Recent.Count >= settings.Limit)
                {
                    // No budget left to start it again. A stalled spectate that stays up is
                    // better than one killed for good, so it is left degraded.
                    return ServiceRestartAction.None;
                }

                ledger.DownReason = StalledReason;
                ledger.DownCause = health.Cause;
                return ServiceRestartAction.Kill;

            default:
                ledger.DownSince = null;
                ledger.DownReason = null;
                ledger.DownCause = null;
                ledger.StaleSince = null;
                ledger.NextRestartAt = null;
                return ServiceRestartAction.None;
        }
    }

    /// <summary>
    /// One restart attempt began at <paramref name="at"/>. It counts against the budget whether
    /// or not it got ready. When it did not, the next <see cref="Decide"/> schedules the next try
    /// with the next backoff, or leaves the role down.
    /// </summary>
    public static void Restarted(
        ServiceRoleRestarts ledger,
        DateTimeOffset at,
        string nonce,
        string failure
    )
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ledger.Count++;
        ledger.Recent.Add(at);
        ledger.LastRestartAt = at;
        ledger.LastReason = ledger.DownReason ?? FailedReason;
        ledger.LastFailure = failure;
        if (!string.IsNullOrWhiteSpace(nonce))
        {
            ledger.Nonce = nonce;
        }

        ledger.DownSince = null;
        ledger.StaleSince = null;
        ledger.NextRestartAt = null;
        if (failure == null)
        {
            ledger.DownReason = null;
            ledger.DownCause = null;
        }
    }

    private static ServiceRestartAction Exhaust(ServiceRoleRestarts ledger, DateTimeOffset now)
    {
        ledger.Exhausted = true;
        ledger.ExhaustedAt = now;
        ledger.NextRestartAt = null;
        return ServiceRestartAction.Exhausted;
    }

    private static void Prune(
        ServiceRoleRestarts ledger,
        DateTimeOffset now,
        ServiceRestartSettings settings
    )
    {
        ledger.Recent ??= new List<DateTimeOffset>();
        ledger.Recent.RemoveAll(at => now - at >= settings.Window);
    }
}
