using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Status;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// Classifies each role from <c>services.json</c>, the process table, and the role's heartbeat.
/// Pure: the clock, the process table, and the heartbeat reader are all passed in.
/// </summary>
public static class ServiceHealthClassifier
{
    private const string RestartStack =
        "run `heroesreplay services stop`, then `heroesreplay services start`.";

    public static ServiceStatusReport Build(
        ServiceLock snapshot,
        Func<int, string> processNameOrNull,
        Func<int, ServiceProcessProbe> probeOrNull,
        Func<ServiceProcessRecord, ServiceReadyReport> readHeartbeat,
        bool stopRequested,
        DateTimeOffset now,
        ServiceHealthSettings settings,
        string environment = null,
        SpectatorStatus spectator = null
    )
    {
        settings ??= new ServiceHealthSettings();
        List<ServiceProcessRecord> recorded = (
            snapshot?.Processes ?? new List<ServiceProcessRecord>()
        )
            .Where(record => record != null && !string.IsNullOrWhiteSpace(record.Name))
            .ToList();

        // Every planned role is listed, then any other recorded role, in plan order.
        var names = ServiceProcessPlan.All.Select(item => item.Name).ToList();
        foreach (ServiceProcessRecord record in recorded)
        {
            if (!names.Contains(record.Name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(record.Name);
            }
        }

        var roles = new List<ServiceRoleHealth>();
        foreach (string name in names)
        {
            ServiceProcessRecord record = recorded.FirstOrDefault(item =>
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)
            );
            bool running =
                record != null
                && record.Pid > 0
                && processNameOrNull != null
                && ServiceProcessPlan
                    .StillRunning(new[] { record }, processNameOrNull, probeOrNull)
                    .Count == 1;
            ServiceReadyReport heartbeat = record == null ? null : readHeartbeat?.Invoke(record);
            roles.Add(Classify(name, record, running, heartbeat, stopRequested, now, settings));
        }

        ServiceRoleState worst = Worst(roles);
        return new ServiceStatusReport
        {
            Ok = roles.All(role =>
                role.State is ServiceRoleState.Ready or ServiceRoleState.Stopped
            ),
            Code = ServiceHealthCodes.For(worst),
            Message = Summarise(roles),
            Environment = string.IsNullOrWhiteSpace(environment) ? null : environment,
            CheckedAt = now,
            StopRequested = stopRequested,
            Roles = roles,
            Spectator = ServiceSpectatorSummary.From(spectator),
        };
    }

    /// <summary>
    /// One role. <paramref name="record"/> is null when <c>services.json</c> does not list it.
    /// </summary>
    public static ServiceRoleHealth Classify(
        string role,
        ServiceProcessRecord record,
        bool running,
        ServiceReadyReport heartbeat,
        bool stopRequested,
        DateTimeOffset now,
        ServiceHealthSettings settings
    )
    {
        settings ??= new ServiceHealthSettings();
        TimeSpan staleAfter = settings.StaleAfter(heartbeat?.HeartbeatIntervalSeconds);
        TimeSpan workThreshold = settings.WorkThreshold(role);
        string work = ServiceHealthSettings.WorkName(role);
        TimeSpan? heartbeatAge = Age(now, heartbeat?.HeartbeatAt);
        TimeSpan? workAge = Age(now, heartbeat?.LastSuccessfulWorkAt);
        int? pid = record?.Pid > 0 ? record.Pid : heartbeat?.Pid;
        var health = new ServiceRoleHealth
        {
            Role = role,
            Expected = record != null,
            Running = running,
            Pid = pid,
            Arguments = record?.Arguments,
            Nonce = record?.Nonce,
            Version = First(heartbeat?.Version, record?.Version),
            ExecutablePath = First(heartbeat?.ExecutablePath, record?.ExecutablePath),
            StartedAt = record?.StartedAt,
            Readiness = heartbeat?.Readiness,
            ReadyAt = heartbeat?.ReadyAt ?? record?.ReadyAt,
            HeartbeatAt = heartbeat?.HeartbeatAt,
            HeartbeatAgeSeconds = Seconds(heartbeatAge),
            StaleAfterSeconds = (long)staleAfter.TotalSeconds,
            LastSuccessfulWorkAt = heartbeat?.LastSuccessfulWorkAt,
            WorkAgeSeconds = Seconds(workAge),
            WorkThresholdSeconds = (long)workThreshold.TotalSeconds,
            LastError = heartbeat?.LastError,
            SessionsWithoutProgress = heartbeat?.SessionsWithoutProgress,
            LastOutcome = heartbeat?.LastOutcome,
            LaunchingSince = heartbeat?.LaunchingSince,
            SessionOutcomes = heartbeat?.SessionOutcomes,
            Dependency = heartbeat?.Dependency,
        };

        if (record == null)
        {
            return With(
                health,
                ServiceRoleState.Stopped,
                "Not running and not expected: `services.json` does not list it.",
                "Run `heroesreplay services start` to start the stack."
            );
        }

        if (!running)
        {
            if (stopRequested || heartbeat?.Readiness == ServiceReadiness.Stopping)
            {
                return With(
                    health,
                    ServiceRoleState.Stopped,
                    $"pid {pid} exited after a stop request.",
                    "Run `heroesreplay services start` to start the stack again."
                );
            }

            string exit =
                heartbeat?.Readiness == ServiceReadiness.Exited
                    ? $"pid {pid} left its loop and exited without a stop request."
                    : $"pid {pid} exited unexpectedly: no clean shutdown was recorded (crash or kill).";
            return With(
                health,
                ServiceRoleState.Failed,
                exit + LastSeen(heartbeatAge) + LastError(heartbeat, now),
                $"Check the {role} console, its log file, or the Aspire logs, then " + RestartStack
            );
        }

        string staleFix =
            "It may be hung or suspended. Run `heroesreplay services stop` (it kills a role still running after 20 seconds), then `heroesreplay services start`.";
        if (heartbeatAge == null)
        {
            return With(
                health,
                ServiceRoleState.Stale,
                $"pid {pid} is running but has no heartbeat file.",
                staleFix
            );
        }

        if (heartbeatAge.Value > staleAfter)
        {
            return With(
                health,
                ServiceRoleState.Stale,
                $"pid {pid} is running but its heartbeat is {Describe(heartbeatAge.Value)} old (limit {Describe(staleAfter)}).",
                staleFix
            );
        }

        string stopping =
            heartbeat.Readiness == ServiceReadiness.Stopping ? " It is stopping." : string.Empty;
        DateTimeOffset? workSince = heartbeat.LastSuccessfulWorkAt ?? heartbeat.ReadyAt;
        TimeSpan? sinceWork = Age(now, workSince);
        string degradedFix =
            $"Check the {role} console, its log file, or the Aspire logs. If it does not recover, "
            + RestartStack;
        TimeSpan stallLimit = settings.LaunchStallThreshold(role);
        if (
            stallLimit > TimeSpan.Zero
            && heartbeat.Readiness != ServiceReadiness.Stopping
            && heartbeat.LaunchingSince is DateTimeOffset launching
            && (
                heartbeat.LastSuccessfulWorkAt == null
                || heartbeat.LastSuccessfulWorkAt.Value < launching
            )
            && Age(now, launching) is TimeSpan launchAge
            && launchAge > stallLimit
        )
        {
            return With(
                health,
                ServiceRoleState.Degraded,
                $"One replay has been launching or loading for {Describe(launchAge)} with no match progress (no match clock, no match on screen; limit {Describe(stallLimit)})."
                    + LastError(heartbeat, now),
                $"A supervisor restarts {role} within its restart budget. Read the {role} log for the launch step and what the client showed. Without a supervisor, "
                    + RestartStack
            ) with
            {
                CauseCode = ServiceHealthCodes.SpectateLaunchStalled,
            };
        }

        // A dependency the role's own probe found rejected or unreachable (#305). Degraded, not
        // failed: the supervisor leaves it alone, so an outage or a bad key is no restart loop.
        ServiceRoleDependency dependency = heartbeat.Dependency;
        if (
            dependency != null
            && ServiceDependencyStates.IsFailure(dependency.State)
            && !string.IsNullOrWhiteSpace(dependency.Code)
        )
        {
            TimeSpan? failingFor = Age(now, dependency.Since);
            string since =
                failingFor == null ? string.Empty : $" For {Describe(failingFor.Value)}.";
            return With(
                health,
                ServiceRoleState.Degraded,
                dependency.Cause + since + LastError(heartbeat, now) + stopping,
                string.IsNullOrWhiteSpace(dependency.Remediation)
                    ? $"Read the {role} log. The role keeps running and clears this itself once its probe passes."
                    : dependency.Remediation
            ) with
            {
                CauseCode = dependency.Code,
            };
        }

        int noProgressLimit = settings.NoProgressSessions(role);
        if (
            noProgressLimit > 0
            && heartbeat.SessionsWithoutProgress is int misses
            && misses >= noProgressLimit
        )
        {
            string last = string.IsNullOrWhiteSpace(heartbeat.LastOutcome)
                ? string.Empty
                : $" The last one ended {heartbeat.LastOutcome}.";
            return With(
                health,
                ServiceRoleState.Degraded,
                $"{misses} replay sessions in a row ended without match progress (no match clock, no award screen; limit {noProgressLimit})."
                    + last
                    + LastError(heartbeat, now)
                    + stopping,
                $"Read the {role} log: each replay logs how it ended. A load timeout means the match clock never read. If it does not recover, "
                    + RestartStack
            ) with
            {
                CauseCode = ServiceHealthCodes.SpectateNoMatchProgress,
            };
        }

        if (!string.IsNullOrWhiteSpace(heartbeat.Concern?.Code))
        {
            TimeSpan? concernAge = Age(now, heartbeat.Concern.Since);
            string since =
                concernAge == null ? string.Empty : $" For {Describe(concernAge.Value)}.";
            return With(
                health,
                ServiceRoleState.Degraded,
                heartbeat.Concern.Cause + since + LastError(heartbeat, now) + stopping,
                $"Read the {role} log. The role keeps running and clears this itself once the cause is gone."
            ) with
            {
                CauseCode = heartbeat.Concern.Code,
            };
        }

        if (sinceWork != null && sinceWork.Value > workThreshold)
        {
            string late =
                heartbeat.LastSuccessfulWorkAt == null
                    ? $"No successful {work} since it became ready {Describe(sinceWork.Value)} ago (limit {Describe(workThreshold)})."
                    : $"Last successful {work} was {Describe(sinceWork.Value)} ago (limit {Describe(workThreshold)}).";
            return With(
                health,
                ServiceRoleState.Degraded,
                late + LastError(heartbeat, now) + stopping,
                degradedFix
            );
        }

        if (
            heartbeat.LastError?.At is DateTimeOffset errorAt
            && (workSince == null || errorAt > workSince.Value)
        )
        {
            return With(
                health,
                ServiceRoleState.Degraded,
                $"The last error is newer than the last successful {work}."
                    + LastError(heartbeat, now)
                    + stopping,
                degradedFix
            );
        }

        string done =
            heartbeat.LastSuccessfulWorkAt == null
                ? $"no {work} yet"
                : $"last {work} {Describe(workAge ?? TimeSpan.Zero)} ago";
        return With(
            health,
            ServiceRoleState.Ready,
            $"Heartbeat {Describe(heartbeatAge.Value)} ago, {done}." + stopping,
            null
        );
    }

    /// <summary>Sets each role's log file from <paramref name="logPath"/> (role name to path).</summary>
    public static ServiceStatusReport WithLogPaths(
        ServiceStatusReport report,
        Func<string, string> logPath
    )
    {
        if (report == null || logPath == null)
        {
            return report;
        }

        return report with
        {
            Roles = report
                .Roles.Select(role => role with { LogPath = logPath(role.Role) })
                .ToList(),
        };
    }

    /// <summary>
    /// Adds the supervisor: whether it runs, and each role's restarts and budget. A failed role
    /// whose budget is exhausted reports <c>service.restart_budget_exhausted</c>, and so does the
    /// envelope. A failed role the running supervisor will restart says when.
    /// <paramref name="liveness"/> is <see cref="ServiceSupervisorFile.Check"/>: whether it runs,
    /// and whether the mutex or <c>supervisor.json</c> decided.
    /// </summary>
    public static ServiceStatusReport WithSupervisor(
        ServiceStatusReport report,
        ServiceSupervisorState state,
        ServiceSupervisorLiveness liveness,
        DateTimeOffset now
    )
    {
        bool running = liveness?.Running == true;
        if (report == null || (state == null && !running))
        {
            return report;
        }

        TimeSpan window = TimeSpan.FromSeconds(Math.Max(0, state?.BudgetWindowSeconds ?? 0));
        var roles = new List<ServiceRoleHealth>();
        foreach (ServiceRoleHealth role in report.Roles)
        {
            ServiceRoleRestarts ledger = state?.Roles?.FirstOrDefault(item =>
                item != null
                && string.Equals(item.Role, role.Role, StringComparison.OrdinalIgnoreCase)
                && (
                    string.IsNullOrWhiteSpace(item.Nonce)
                    || string.Equals(item.Nonce, role.Nonce, StringComparison.Ordinal)
                )
            );
            if (ledger == null)
            {
                roles.Add(role);
                continue;
            }

            int used = ledger.Recent?.Count(at => now - at < window) ?? 0;
            ServiceRoleHealth annotated = role with
            {
                Restarts = new ServiceRoleRestartStatus
                {
                    Count = ledger.Count,
                    LastRestartAt = ledger.LastRestartAt,
                    LastReason = ledger.LastReason,
                    LastFailure = ledger.LastFailure,
                    NextRestartAt = running ? ledger.NextRestartAt : null,
                    BudgetUsed = used,
                    BudgetLimit = state.Budget,
                    BudgetWindowSeconds = state.BudgetWindowSeconds,
                    BudgetExhausted = ledger.Exhausted,
                    ExhaustedAt = ledger.ExhaustedAt,
                },
            };
            string log = string.IsNullOrWhiteSpace(role.LogPath)
                ? $"the {role.Role} log"
                : role.LogPath;
            if (role.State == ServiceRoleState.Failed && ledger.Exhausted)
            {
                annotated = annotated with
                {
                    Code = ServiceHealthCodes.RestartBudgetExhausted,
                    Cause =
                        $"The supervisor restarted it {ledger.Count} times and its budget of {state.Budget} restarts in {Describe(window)} is exhausted, so it stays down. "
                        + role.Cause,
                    Remediation =
                        $"Read {log}, fix the cause, then run `heroesreplay services stop` and `heroesreplay services start --supervise`.",
                };
            }
            else if (
                role.State == ServiceRoleState.Failed
                && running
                && !state.Stopping
                && ledger.NextRestartAt is DateTimeOffset next
            )
            {
                annotated = annotated with
                {
                    Cause =
                        role.Cause
                        + $" The supervisor restarts it in {Describe(next - now)} (restart {used + 1} of {state.Budget} in {Describe(window)}).",
                    Remediation =
                        $"Nothing yet: the supervisor restarts it. If it keeps failing, read {log}.",
                };
            }

            roles.Add(annotated);
        }

        List<string> exhausted = roles
            .Where(role => role.Code == ServiceHealthCodes.RestartBudgetExhausted)
            .Select(role => role.Role)
            .ToList();
        return report with
        {
            Roles = roles,
            Code = exhausted.Count > 0 ? ServiceHealthCodes.RestartBudgetExhausted : report.Code,
            Message =
                exhausted.Count > 0
                    ? $"{report.Message} Restart budget exhausted: {string.Join(", ", exhausted)}."
                    : report.Message,
            Supervisor = new ServiceSupervisorSummary
            {
                Running = running,
                SeenVia = liveness?.SeenVia,
                Detail = liveness?.Detail,
                Pid = state?.Pid,
                StartedAt = state?.StartedAt,
                UpdatedAt = state?.UpdatedAt,
                Stopping = state?.Stopping == true,
                LogPath = state?.LogPath,
                Supervised = state?.Supervised ?? new List<string>(),
                BackoffSeconds = state?.BackoffSeconds ?? new List<long>(),
                Budget = state?.Budget ?? 0,
                BudgetWindowSeconds = state?.BudgetWindowSeconds ?? 0,
                StaleRestartAfterSeconds = state?.StaleRestartAfterSeconds ?? 0,
            },
        };
    }

    /// <summary>failed, then stale, then degraded, then ready, then stopped.</summary>
    public static ServiceRoleState Worst(IEnumerable<ServiceRoleHealth> roles)
    {
        var states = roles?.Select(role => role.State).ToList() ?? new List<ServiceRoleState>();
        foreach (
            ServiceRoleState state in new[]
            {
                ServiceRoleState.Failed,
                ServiceRoleState.Stale,
                ServiceRoleState.Degraded,
                ServiceRoleState.Ready,
            }
        )
        {
            if (states.Contains(state))
            {
                return state;
            }
        }

        return ServiceRoleState.Stopped;
    }

    public static string Describe(TimeSpan span)
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
            return span.Seconds == 0
                ? $"{(int)span.TotalMinutes}m"
                : $"{(int)span.TotalMinutes}m {span.Seconds}s";
        }

        return span.Minutes == 0
            ? $"{(int)span.TotalHours}h"
            : $"{(int)span.TotalHours}h {span.Minutes}m";
    }

    private static string Summarise(IReadOnlyList<ServiceRoleHealth> roles)
    {
        if (roles.All(role => role.State == ServiceRoleState.Stopped && !role.Expected))
        {
            return "No HeroesReplay services are running.";
        }

        int expected = roles.Count(role => role.Expected);
        int ready = roles.Count(role => role.State == ServiceRoleState.Ready);
        IEnumerable<string> others = roles
            .Where(role => role.State != ServiceRoleState.Ready)
            .GroupBy(role => role.State)
            .OrderBy(group => Rank(group.Key))
            .Select(group =>
                $"{string.Join(", ", group.Select(role => role.Role))} {group.Key.ToString().ToLowerInvariant()}"
            );
        string rest = string.Join("; ", others);
        return string.IsNullOrEmpty(rest)
            ? $"{ready} of {expected} roles ready."
            : $"{ready} of {expected} roles ready; {rest}.";
    }

    private static int Rank(ServiceRoleState state) =>
        state switch
        {
            ServiceRoleState.Failed => 0,
            ServiceRoleState.Stale => 1,
            ServiceRoleState.Degraded => 2,
            ServiceRoleState.Ready => 3,
            _ => 4,
        };

    private static ServiceRoleHealth With(
        ServiceRoleHealth health,
        ServiceRoleState state,
        string cause,
        string remediation
    ) =>
        health with
        {
            State = state,
            Code = ServiceHealthCodes.For(state),
            Cause = cause,
            Remediation = remediation,
        };

    private static string LastSeen(TimeSpan? heartbeatAge) =>
        heartbeatAge == null
            ? string.Empty
            : $" Its last heartbeat was {Describe(heartbeatAge.Value)} ago.";

    private static string LastError(ServiceReadyReport heartbeat, DateTimeOffset now)
    {
        ServiceRoleError error = heartbeat?.LastError;
        if (string.IsNullOrWhiteSpace(error?.Message))
        {
            return string.Empty;
        }

        TimeSpan? age = Age(now, error.At);
        return age == null
            ? $" Last error: {error.Message}"
            : $" Last error {Describe(age.Value)} ago: {error.Message}";
    }

    private static TimeSpan? Age(DateTimeOffset now, DateTimeOffset? at)
    {
        if (at == null)
        {
            return null;
        }

        TimeSpan age = now - at.Value;
        return age < TimeSpan.Zero ? TimeSpan.Zero : age;
    }

    private static long? Seconds(TimeSpan? span) =>
        span == null ? null : (long)Math.Floor(span.Value.TotalSeconds);

    private static string First(string preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
}
