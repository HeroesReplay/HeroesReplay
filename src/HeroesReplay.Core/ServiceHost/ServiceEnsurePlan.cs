using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>Stable result codes of <c>services ensure</c> (#306).</summary>
public static class ServiceEnsureCodes
{
    /// <summary>Every requested role is up from this install, and a supervisor runs when one was asked for.</summary>
    public const string Noop = "service.ensure_noop";

    /// <summary>The missing roles were started, or a supervisor was attached, or both.</summary>
    public const string Started = "service.ensure_started";

    /// <summary>A role runs from another install path or version. Builds are not mixed.</summary>
    public const string Mismatch = "service.ensure_mismatch";

    /// <summary><c>services stop</c> left its stop file down: a stop is pending or in progress.</summary>
    public const string StopPending = "service.ensure_stop_pending";

    /// <summary>A requested role used its restart budget; it stays down until stop and start.</summary>
    public const string BudgetExhausted = "service.ensure_budget_exhausted";

    /// <summary>
    /// A requested role is down while a supervisor runs (in any session). The supervisor owns its
    /// restarts; ensure does not race it.
    /// </summary>
    public const string SupervisorRunning = "service.ensure_supervisor_running";

    /// <summary>A requested role runs but its heartbeat is stale. Ensure never stops a running role.</summary>
    public const string Stale = "service.ensure_stale";

    /// <summary>A start failed. What this ensure started was stopped again; nothing else was touched.</summary>
    public const string StartFailed = "service.ensure_start_failed";

    /// <summary>Another <c>services ensure</c> is running.</summary>
    public const string Busy = "service.ensure_busy";
}

/// <summary>What <c>services ensure</c> does with one requested role.</summary>
public static class ServiceEnsureActions
{
    /// <summary>It is up (ready or degraded) from this install: left alone.</summary>
    public const string Running = "running";

    /// <summary>It is down (failed, exited, or never started) and is started.</summary>
    public const string Start = "start";

    /// <summary>It was started and is ready.</summary>
    public const string Started = "started";

    /// <summary>It did not start, or it was stopped again because another role did not.</summary>
    public const string StartFailed = "start_failed";

    /// <summary>It blocks the ensure (stale, budget exhausted, another build, or the supervisor's).</summary>
    public const string Blocked = "blocked";
}

/// <summary>One requested role in a <see cref="ServiceEnsureReport"/>.</summary>
public sealed record ServiceEnsureRole
{
    public string Role { get; init; }

    /// <summary>Its state before the ensure, as <c>services status</c> classified it.</summary>
    public ServiceRoleState State { get; init; }
    public string CauseCode { get; init; }

    /// <summary><see cref="ServiceEnsureActions"/>.</summary>
    public string Action { get; init; }
    public int? Pid { get; init; }
    public string Version { get; init; }
    public string ExecutablePath { get; init; }
    public string Detail { get; init; }
}

/// <summary>
/// The <c>services ensure</c> result, in the <c>services status</c> envelope:
/// <see cref="SchemaVersion"/>, <see cref="Ok"/>, <see cref="Code"/>.
/// </summary>
public sealed record ServiceEnsureReport : ICliResult
{
    public const int CurrentSchemaVersion = CliJson.SchemaVersion;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public bool Ok { get; init; }

    /// <summary><see cref="ServiceEnsureCodes"/>.</summary>
    public string Code { get; init; }
    public string Message { get; init; }

    /// <summary>What to do when it refused or failed. Null when it is ok.</summary>
    public string Remediation { get; init; }
    public string Environment { get; init; }
    public DateTimeOffset CheckedAt { get; init; }
    public bool StopRequested { get; init; }
    public bool Supervise { get; init; }

    /// <summary>A supervisor ran (in any session) when the ensure began.</summary>
    public bool SupervisorRunning { get; init; }

    /// <summary>This process becomes the supervisor once the report is printed.</summary>
    public bool SupervisorAttached { get; init; }
    public IReadOnlyList<string> Requested { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Started { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ServiceEnsureRole> Roles { get; init; } = Array.Empty<ServiceEnsureRole>();

    [JsonIgnore]
    public int ExitCode => Ok ? 0 : 1;

    public string ToJson() => CliJson.Serialize(this);

    public static ServiceEnsureReport FromJson(string json) =>
        JsonSerializer.Deserialize<ServiceEnsureReport>(json, CliJson.Options);
}

/// <summary>
/// The <c>services ensure</c> decision (#306), pure: the <c>services status</c> report (with the
/// supervisor), the stop file, and this install. Refusals come first and never stop anything: a
/// pending stop, a role from another install or version, a requested role whose restart budget is
/// used, a stale requested role, and a down role while a supervisor runs (the supervisor owns
/// restarts). Otherwise the down requested roles are started and, with <c>--supervise</c>, a
/// supervisor is attached when none runs.
/// </summary>
public static class ServiceEnsurePlan
{
    private const string StopThenEnsure =
        "Run `heroesreplay services stop`, then `heroesreplay services ensure` (or `services start`).";

    /// <param name="status">Every role as <c>services status</c> classifies it, with the supervisor.</param>
    /// <param name="requested">The roles to ensure, in plan order.</param>
    /// <param name="liveness">Whether a supervisor runs in this session or another.</param>
    /// <param name="executablePath">This <c>heroesreplay.exe</c>.</param>
    /// <param name="version">This build's version, as <c>services start</c> records it.</param>
    /// <param name="supervise"><c>--supervise</c>.</param>
    public static ServiceEnsureReport Decide(
        ServiceStatusReport status,
        IReadOnlyList<string> requested,
        ServiceSupervisorLiveness liveness,
        string executablePath,
        string version,
        bool supervise
    )
    {
        ArgumentNullException.ThrowIfNull(status);
        requested ??= ServiceProcessPlan.Names;
        bool supervisorRunning = liveness?.Running == true;
        var report = new ServiceEnsureReport
        {
            Environment = status.Environment,
            CheckedAt = status.CheckedAt,
            StopRequested = status.StopRequested,
            Supervise = supervise,
            SupervisorRunning = supervisorRunning,
            Requested = requested.ToList(),
        };

        if (status.StopRequested)
        {
            return Refuse(
                report,
                ServiceEnsureCodes.StopPending,
                "A stop is pending (services.stop), so nothing is started.",
                "Wait for `heroesreplay services stop` to finish. If none is running, run `heroesreplay services stop` again: it clears the stop file.",
                Describe(status, requested, _ => ServiceEnsureActions.Blocked)
            );
        }

        // Every live role counts, requested or not: one stack runs one build.
        List<ServiceRoleHealth> foreign = status
            .Roles.Where(role => role.Expected && role.Running)
            .Where(role => OtherBuild(role, executablePath, version) != null)
            .ToList();
        if (foreign.Count > 0)
        {
            string which = string.Join(
                "; ",
                foreign.Select(role =>
                    $"{role.Role} pid {role.Pid} runs {OtherBuild(role, executablePath, version)}"
                )
            );
            return Refuse(
                report,
                ServiceEnsureCodes.Mismatch,
                $"Builds are not mixed: {which}, not this install ({executablePath}, version {version}).",
                $"Run ensure from the install that runs, or replace that stack: {StopThenEnsure}",
                Describe(
                    status,
                    requested,
                    role =>
                        foreign.Contains(role) ? ServiceEnsureActions.Blocked
                        : Up(role) ? ServiceEnsureActions.Running
                        : ServiceEnsureActions.Start
                ),
                foreign
            );
        }

        List<ServiceRoleHealth> roles = Requested(status, requested);
        List<ServiceRoleHealth> exhausted = roles
            .Where(role =>
                role.Restarts?.BudgetExhausted == true
                || role.Code == ServiceHealthCodes.RestartBudgetExhausted
            )
            .ToList();
        if (exhausted.Count > 0)
        {
            return Refuse(
                report,
                ServiceEnsureCodes.BudgetExhausted,
                $"{Names(exhausted)} used the restart budget and stays down until the stack is stopped and started again.",
                $"Read the role log for the cause and fix it, then run `heroesreplay services stop` and `heroesreplay services start --supervise`.",
                Describe(status, requested, role => Action(role, exhausted))
            );
        }

        List<ServiceRoleHealth> stale = roles
            .Where(role => role.State == ServiceRoleState.Stale)
            .ToList();
        if (stale.Count > 0)
        {
            return Refuse(
                report,
                ServiceEnsureCodes.Stale,
                $"{Names(stale)} runs but its heartbeat is stale. Ensure never stops a running role.",
                $"A supervisor kills and restarts a stale role after ServiceRestart:StaleRestartAfter. Otherwise: {StopThenEnsure}",
                Describe(status, requested, role => Action(role, stale))
            );
        }

        List<ServiceRoleHealth> down = roles.Where(role => !Up(role)).ToList();
        if (supervisorRunning)
        {
            if (down.Count == 0)
            {
                return report with
                {
                    Ok = true,
                    Code = ServiceEnsureCodes.Noop,
                    Message =
                        $"Nothing to do: {Names(roles)} {(roles.Count == 1 ? "is" : "are")} up from this install and a supervisor runs.",
                    Roles = Describe(status, requested, _ => ServiceEnsureActions.Running),
                };
            }

            return Refuse(
                report,
                ServiceEnsureCodes.SupervisorRunning,
                $"A supervisor runs ({Via(liveness, status.Supervisor)}), so ensure does not start {Names(down)}: {string.Join(" ", down.Select(role => Handoff(role, status.Supervisor)))}",
                "Wait for the supervisor's restart and run ensure again. For a role it does not supervise: "
                    + StopThenEnsure
                    + " with `--supervise`.",
                Describe(
                    status,
                    requested,
                    role =>
                        down.Contains(role)
                            ? ServiceEnsureActions.Blocked
                            : ServiceEnsureActions.Running,
                    detail: role => down.Contains(role) ? Handoff(role, status.Supervisor) : null
                )
            );
        }

        bool attach = supervise;
        if (down.Count == 0 && !attach)
        {
            return report with
            {
                Ok = true,
                Code = ServiceEnsureCodes.Noop,
                Message =
                    $"Nothing to do: {Names(roles)} {(roles.Count == 1 ? "is" : "are")} up from this install.",
                Roles = Describe(status, requested, _ => ServiceEnsureActions.Running),
            };
        }

        string starting = down.Count == 0 ? null : $"Starting {Names(down)}";
        string attaching = attach ? "attaching a supervisor" : null;
        return report with
        {
            Ok = true,
            Code = ServiceEnsureCodes.Started,
            Message =
                string.Join(" and ", new[] { starting, attaching }.Where(part => part != null))
                + ".",
            SupervisorAttached = attach,
            Roles = Describe(
                status,
                requested,
                role =>
                    down.Contains(role) ? ServiceEnsureActions.Start : ServiceEnsureActions.Running
            ),
        };
    }

    /// <summary>The requested roles the plan starts, in plan order.</summary>
    public static IReadOnlyList<string> ToStart(ServiceEnsureReport plan) =>
        plan?.Roles?.Where(role => role.Action == ServiceEnsureActions.Start)
            .Select(role => role.Role)
            .ToList()
        ?? new List<string>();

    /// <summary>Ready or degraded: alive, heartbeating, and left alone.</summary>
    public static bool Up(ServiceRoleHealth role) =>
        role.State is ServiceRoleState.Ready or ServiceRoleState.Degraded;

    /// <summary>Null when <paramref name="role"/> is this build; otherwise what it runs.</summary>
    public static string OtherBuild(ServiceRoleHealth role, string executablePath, string version)
    {
        var differences = new List<string>();
        if (
            !string.IsNullOrWhiteSpace(role.ExecutablePath)
            && !string.IsNullOrWhiteSpace(executablePath)
            && !SamePath(role.ExecutablePath, executablePath)
        )
        {
            differences.Add(role.ExecutablePath);
        }

        if (
            !string.IsNullOrWhiteSpace(role.Version)
            && !string.IsNullOrWhiteSpace(version)
            && !string.Equals(role.Version, version, StringComparison.Ordinal)
        )
        {
            differences.Add("version " + role.Version);
        }

        return differences.Count == 0 ? null : string.Join(", ", differences);
    }

    private static ServiceEnsureReport Refuse(
        ServiceEnsureReport report,
        string code,
        string message,
        string remediation,
        IReadOnlyList<ServiceEnsureRole> roles,
        IEnumerable<ServiceRoleHealth> extra = null
    )
    {
        // A refusal starts nothing, so a down role is blocked too, never "start".
        List<ServiceEnsureRole> all = roles
            .Select(role =>
                role.Action == ServiceEnsureActions.Start
                    ? role with
                    {
                        Action = ServiceEnsureActions.Blocked,
                    }
                    : role
            )
            .ToList();
        foreach (ServiceRoleHealth role in extra ?? Enumerable.Empty<ServiceRoleHealth>())
        {
            if (!all.Any(item => string.Equals(item.Role, role.Role, StringComparison.Ordinal)))
            {
                all.Add(Row(role, ServiceEnsureActions.Blocked, null));
            }
        }

        return report with
        {
            Ok = false,
            Code = code,
            Message = message,
            Remediation = remediation,
            Roles = all,
        };
    }

    private static List<ServiceRoleHealth> Requested(
        ServiceStatusReport status,
        IReadOnlyList<string> requested
    ) =>
        requested
            .Select(name =>
                status.Roles.FirstOrDefault(role =>
                    string.Equals(role.Role, name, StringComparison.OrdinalIgnoreCase)
                )
                ?? new ServiceRoleHealth
                {
                    Role = name,
                    State = ServiceRoleState.Stopped,
                    Code = ServiceHealthCodes.Stopped,
                }
            )
            .ToList();

    private static IReadOnlyList<ServiceEnsureRole> Describe(
        ServiceStatusReport status,
        IReadOnlyList<string> requested,
        Func<ServiceRoleHealth, string> action,
        Func<ServiceRoleHealth, string> detail = null
    ) =>
        Requested(status, requested)
            .Select(role => Row(role, action(role), detail?.Invoke(role)))
            .ToList();

    private static ServiceEnsureRole Row(ServiceRoleHealth role, string action, string detail) =>
        new()
        {
            Role = role.Role,
            State = role.State,
            CauseCode = role.CauseCode,
            Action = action,
            Pid = role.Running ? role.Pid : null,
            Version = role.Version,
            ExecutablePath = role.ExecutablePath,
            Detail = detail ?? role.Cause,
        };

    private static string Action(ServiceRoleHealth role, List<ServiceRoleHealth> blocking) =>
        blocking.Contains(role) ? ServiceEnsureActions.Blocked
        : Up(role) ? ServiceEnsureActions.Running
        : ServiceEnsureActions.Start;

    /// <summary>What the running supervisor does about one down role.</summary>
    private static string Handoff(ServiceRoleHealth role, ServiceSupervisorSummary supervisor)
    {
        bool supervised =
            supervisor?.Supervised?.Contains(role.Role, StringComparer.OrdinalIgnoreCase) == true;
        if (!supervised)
        {
            return $"{role.Role} is {State(role)} and the supervisor does not supervise it.";
        }

        if (role.State != ServiceRoleState.Failed)
        {
            return $"{role.Role} is {State(role)}; the supervisor leaves a stopped role alone.";
        }

        if (role.Restarts?.NextRestartAt is DateTimeOffset next)
        {
            return $"{role.Role} is {State(role)}; the supervisor restarts it at {next:u}.";
        }

        return $"{role.Role} is {State(role)}; the supervisor restarts it.";
    }

    private static string Via(ServiceSupervisorLiveness liveness, ServiceSupervisorSummary summary)
    {
        string pid = summary?.Pid is int value ? $"pid {value}, " : string.Empty;
        return liveness?.SeenVia == ServiceSupervisorLiveness.ViaStateFile
            ? pid + "seen via supervisor.json"
            : pid + "seen via its mutex";
    }

    private static string State(ServiceRoleHealth role) =>
        role.Expected ? role.State.ToString().ToLowerInvariant() : "not started";

    private static string Names(IEnumerable<ServiceRoleHealth> roles) =>
        string.Join(", ", roles.Select(role => role.Role));

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase
            );
        }
        catch (Exception)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
