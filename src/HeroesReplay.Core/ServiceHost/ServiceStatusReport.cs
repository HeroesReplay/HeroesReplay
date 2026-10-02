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

    [JsonIgnore]
    public int ExitCode => Ok ? 0 : 1;

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static ServiceStatusReport FromJson(string json) =>
        JsonSerializer.Deserialize<ServiceStatusReport>(json, Json);
}
