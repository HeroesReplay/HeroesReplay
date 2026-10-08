using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeroesReplay.Core.Status;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>How <c>services status</c> sees one role.</summary>
public enum ServiceRoleState
{
    /// <summary>Running, heartbeating, and doing its work.</summary>
    Ready,

    /// <summary>Running and heartbeating, but its last successful work is too old or an error is newer.</summary>
    Degraded,

    /// <summary>The process is alive but its heartbeat is older than the stale limit.</summary>
    Stale,

    /// <summary>Not running and not expected: never started, or exited after a stop request.</summary>
    Stopped,

    /// <summary>Expected to run but the process is gone without a stop request.</summary>
    Failed,
}

/// <summary>Stable result codes for <c>services status --output json</c>.</summary>
public static class ServiceHealthCodes
{
    public const string Ready = "service.ready";
    public const string Degraded = "service.degraded";
    public const string Stale = "service.stale";
    public const string Stopped = "service.stopped";
    public const string Failed = "service.failed";

    /// <summary>
    /// A failed role the supervisor restarted as often as its budget allows. It stays down until
    /// the stack is stopped and started again.
    /// </summary>
    public const string RestartBudgetExhausted = "service.restart_budget_exhausted";

    /// <summary>
    /// The cause of a degraded spectate role: the last sessions in a row ended without match
    /// progress (<see cref="ServiceRoleHealth.CauseCode"/>).
    /// </summary>
    public const string SpectateNoMatchProgress = "spectate.no_match_progress";

    /// <summary>
    /// The cause of a degraded spectate role that the supervisor restarts: one replay's launch
    /// and loading phase went past <c>ServiceHealth:SpectateLaunchStallThreshold</c> without
    /// match progress (#249).
    /// </summary>
    public const string SpectateLaunchStalled = "spectate.launch_stalled";

    public static string For(ServiceRoleState state) =>
        state switch
        {
            ServiceRoleState.Ready => Ready,
            ServiceRoleState.Degraded => Degraded,
            ServiceRoleState.Stale => Stale,
            ServiceRoleState.Failed => Failed,
            _ => Stopped,
        };
}

public sealed record ServiceRoleHealth
{
    public string Role { get; init; }
    public ServiceRoleState State { get; init; }
    public string Code { get; init; }

    /// <summary>What was seen, in a sentence a person or an agent can act on.</summary>
    public string Cause { get; init; }

    /// <summary>
    /// A stable code for a specific cause, next to the state's <see cref="Code"/>
    /// (<c>spectate.no_match_progress</c>). Null when the state says it all.
    /// </summary>
    public string CauseCode { get; init; }

    /// <summary>What to do about it. Null when the role is ready.</summary>
    public string Remediation { get; init; }

    /// <summary>The role is recorded in <c>services.json</c>, so it should be running.</summary>
    public bool Expected { get; init; }
    public bool Running { get; init; }
    public int? Pid { get; init; }
    public string Arguments { get; init; }
    public string Nonce { get; init; }
    public string Version { get; init; }
    public string ExecutablePath { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public string Readiness { get; init; }
    public DateTimeOffset? ReadyAt { get; init; }
    public DateTimeOffset? HeartbeatAt { get; init; }
    public long? HeartbeatAgeSeconds { get; init; }
    public long StaleAfterSeconds { get; init; }
    public DateTimeOffset? LastSuccessfulWorkAt { get; init; }
    public long? WorkAgeSeconds { get; init; }
    public long WorkThresholdSeconds { get; init; }
    public ServiceRoleError LastError { get; init; }

    /// <summary>Spectate: replay sessions in a row without match progress. Null for other roles.</summary>
    public int? SessionsWithoutProgress { get; init; }

    /// <summary>Spectate: how the last replay session ended.</summary>
    public string LastOutcome { get; init; }

    /// <summary>Spectate: when the current launch and loading phase began. Null outside it.</summary>
    public DateTimeOffset? LaunchingSince { get; init; }

    /// <summary>Spectate: replay sessions this process ended, by outcome.</summary>
    public IReadOnlyDictionary<string, int> SessionOutcomes { get; init; }

    /// <summary>The role's newest log file, or the file it writes today when there is none yet.</summary>
    public string LogPath { get; init; }

    /// <summary>The supervisor's restarts of this role. Null when no supervisor has run it.</summary>
    public ServiceRoleRestartStatus Restarts { get; init; }
}

/// <summary>One role's restarts and budget, as <c>services status</c> reports them.</summary>
public sealed record ServiceRoleRestartStatus
{
    public int Count { get; init; }
    public DateTimeOffset? LastRestartAt { get; init; }

    /// <summary><c>failed</c> or <c>stale</c>.</summary>
    public string LastReason { get; init; }

    /// <summary>Why the last attempt did not get ready. Null when it did.</summary>
    public string LastFailure { get; init; }

    /// <summary>When the supervisor restarts the role next. Null unless it is waiting out a backoff.</summary>
    public DateTimeOffset? NextRestartAt { get; init; }
    public int BudgetUsed { get; init; }
    public int BudgetLimit { get; init; }
    public long BudgetWindowSeconds { get; init; }
    public bool BudgetExhausted { get; init; }
    public DateTimeOffset? ExhaustedAt { get; init; }
}

/// <summary>The supervisor next to the roles: whether it runs, and its rules.</summary>
public sealed record ServiceSupervisorSummary
{
    public bool Running { get; init; }

    /// <summary>
    /// How <see cref="Running"/> was decided: <c>mutex</c>, or <c>supervisor.json</c> when this
    /// session cannot see the mutex (an SSH logon). Null when neither showed a supervisor.
    /// </summary>
    public string SeenVia { get; init; }

    /// <summary>Why <c>supervisor.json</c> did or did not count. Null when the mutex decided.</summary>
    public string Detail { get; init; }
    public int? Pid { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public bool Stopping { get; init; }
    public string LogPath { get; init; }
    public IReadOnlyList<string> Supervised { get; init; } = Array.Empty<string>();
    public IReadOnlyList<long> BackoffSeconds { get; init; } = Array.Empty<long>();
    public int Budget { get; init; }
    public long BudgetWindowSeconds { get; init; }
    public long StaleRestartAfterSeconds { get; init; }
}

/// <summary>The spectator's own status file, summarised next to the roles.</summary>
public sealed record ServiceSpectatorSummary
{
    public string Phase { get; init; }
    public bool SpectatorRunning { get; init; }
    public bool SnapshotStale { get; init; }
    public int? ReplayId { get; init; }
    public string Map { get; init; }
    public string Timer { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int? CompletedReplayId { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public int? CompletedWinnerTeam { get; init; }

    public static ServiceSpectatorSummary From(SpectatorStatus status) =>
        status == null
            ? null
            : new ServiceSpectatorSummary
            {
                Phase = status.Phase,
                SpectatorRunning = status.SpectatorRunning,
                SnapshotStale = status.SnapshotStale,
                ReplayId = status.ReplayId,
                Map = status.Map,
                Timer = status.Timer,
                UpdatedAt = status.UpdatedAt,
                CompletedReplayId = status.CompletedReplayId,
                CompletedAt = status.CompletedAt,
                CompletedWinnerTeam = status.CompletedWinnerTeam,
            };
}

/// <summary>
/// The <c>services status</c> result envelope. <see cref="Ok"/> is false when any role is
/// failed, stale, or degraded. <see cref="Code"/> is the worst role's code.
/// </summary>
public sealed record ServiceStatusReport
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public bool Ok { get; init; }
    public string Code { get; init; }
    public string Message { get; init; }
    public string Environment { get; init; }
    public DateTimeOffset CheckedAt { get; init; }

    /// <summary><c>services stop</c> left its stop file: roles are expected to exit.</summary>
    public bool StopRequested { get; init; }
    public IReadOnlyList<ServiceRoleHealth> Roles { get; init; } = Array.Empty<ServiceRoleHealth>();
    public ServiceSpectatorSummary Spectator { get; init; }

    /// <summary>Null when no supervisor runs and none left a state file.</summary>
    public ServiceSupervisorSummary Supervisor { get; init; }

    /// <summary>Memory and leaking process counts (#251). Warnings here do not change <see cref="Ok"/>.</summary>
    public MachineHealthReport Machine { get; init; }

    [JsonIgnore]
    public int ExitCode => Ok ? 0 : 1;

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static ServiceStatusReport FromJson(string json) =>
        JsonSerializer.Deserialize<ServiceStatusReport>(json, Json);
}
