using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>
/// <c>services ensure</c> (#306): make the requested roles run from this install, starting only
/// the ones that are down, through the same launch as <c>services start</c> and the supervisor
/// (prerequisites, a new nonce, the ready file and first heartbeat). It never stops a role that
/// was running. When a start fails, the roles this ensure started are stopped again and
/// <c>services.json</c> is put back, so a failed ensure leaves the stack as it found it.
/// <see cref="ServiceEnsurePlan"/> decides; this applies the decision. Tests replace each step.
/// </summary>
public sealed class ServiceEnsure
{
    public string LockPath { get; init; } = ServiceLockStore.DefaultPath;
    public string ExecutablePath { get; init; }
    public string Version { get; init; }

    /// <summary>The roles to ensure, in plan order. Null is every role.</summary>
    public IReadOnlyList<string> Roles { get; init; }
    public bool Supervise { get; init; }
    public string Environment { get; init; }
    public ServiceHealthSettings Health { get; init; } = new();
    public TimeProvider Time { get; init; } = TimeProvider.System;
    public Func<int, string> ProcessNameOrNull { get; init; }
    public Func<int, ServiceProcessProbe> Probe { get; init; }
    public Func<ServiceProcessRecord, ServiceReadyReport> ReadHeartbeat { get; init; }

    /// <summary>True while <c>services.stop</c> is down.</summary>
    public Func<bool> StopRequested { get; init; }

    /// <summary><c>supervisor.json</c>, for the restart budget and what the supervisor supervises.</summary>
    public Func<ServiceSupervisorState> ReadSupervisor { get; init; }

    /// <summary>Whether a supervisor runs in this session or another (<see cref="ServiceSupervisorFile.Check"/>).</summary>
    public Func<ServiceSupervisorLiveness> SupervisorLiveness { get; init; }

    /// <summary>
    /// Starts one role: the <c>services start</c> launch (<see cref="ServiceSupervisor.Restart"/>).
    /// The callback sees the record as soon as the process has a pid.
    /// </summary>
    public Func<string, Action<ServiceProcessRecord>, ServiceLaunch> Launch { get; init; }

    /// <summary>Once, before the first start: the dashboard, and the OBS collection when spectate starts.</summary>
    public Action<IReadOnlyList<string>> BeforeStart { get; init; }

    /// <summary>Stops a role this ensure started, when a later one fails.</summary>
    public Action<int> Kill { get; init; }

    /// <summary>Removes the ready file of a record that is gone.</summary>
    public Action<ServiceProcessRecord> DeleteHeartbeat { get; init; }

    /// <summary>Progress lines (each start). JSON output sends them to stderr.</summary>
    public TextWriter Log { get; init; } = TextWriter.Null;

    /// <summary>Reads the stack and decides. Starts nothing.</summary>
    public ServiceEnsureReport Plan()
    {
        DateTimeOffset now = Time.GetUtcNow();
        bool stopRequested = StopRequested?.Invoke() == true;
        ServiceSupervisorLiveness liveness =
            SupervisorLiveness?.Invoke() ?? ServiceSupervisorLiveness.None;
        ServiceStatusReport status = ServiceHealthClassifier.Build(
            ServiceLockStore.TryLoad(LockPath),
            ProcessNameOrNull,
            Probe,
            ReadHeartbeat ?? (record => ServiceReadyFile.TryRead(record)),
            stopRequested,
            now,
            Health,
            Environment
        );
        status = ServiceHealthClassifier.WithSupervisor(
            status,
            ReadSupervisor?.Invoke(),
            liveness,
            now
        );
        return ServiceEnsurePlan.Decide(
            status,
            Roles ?? ServiceProcessPlan.Names,
            liveness,
            ExecutablePath,
            Version,
            Supervise
        );
    }

    /// <summary>Starts the roles <paramref name="plan"/> marks to start, in plan order.</summary>
    public ServiceEnsureReport Apply(ServiceEnsureReport plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        IReadOnlyList<string> toStart = ServiceEnsurePlan.ToStart(plan);
        if (!plan.Ok || toStart.Count == 0)
        {
            return plan;
        }

        BeforeStart?.Invoke(toStart);
        var rows = plan.Roles.ToDictionary(role => role.Role, StringComparer.OrdinalIgnoreCase);
        var started = new List<(ServiceProcessRecord Record, ServiceProcessRecord Previous)>();
        foreach (string role in toStart)
        {
            ServiceProcessRecord previous = Find(ServiceLockStore.TryLoad(LockPath), role);
            if (previous != null)
            {
                DeleteHeartbeat?.Invoke(previous);
            }

            ServiceLaunch launch;
            try
            {
                launch =
                    Launch?.Invoke(role, Record)
                    ?? new ServiceLaunch(null, false, false, "No launcher was given.");
            }
            catch (Exception e)
            {
                launch = new ServiceLaunch(null, false, false, $"{role} failed: {e.Message}");
            }

            if (launch.Ready && launch.Record != null)
            {
                Record(launch.Record);
                started.Add((launch.Record, previous));
                rows[role] = rows[role] with
                {
                    Action = ServiceEnsureActions.Started,
                    Pid = launch.Record.Pid,
                    Version = launch.Record.Version,
                    ExecutablePath = launch.Record.ExecutablePath,
                    Detail = $"Started pid {launch.Record.Pid}.",
                };
                Log.WriteLine($"Ensured {role}: started pid {launch.Record.Pid}.");
                continue;
            }

            if (launch.Cancelled)
            {
                // services stop owns what is in services.json now; it stops this role too.
                rows[role] = rows[role] with
                {
                    Action = ServiceEnsureActions.StartFailed,
                    Pid = launch.Record?.Pid,
                    Detail = launch.Failure,
                };
                return plan with
                {
                    Ok = false,
                    Code = ServiceEnsureCodes.StopPending,
                    Message =
                        $"A stop was requested while {role} was starting. `heroesreplay services stop` stops it with the rest.",
                    Remediation =
                        "Let `heroesreplay services stop` finish, then run `heroesreplay services ensure` again.",
                    SupervisorAttached = false,
                    Started = started.Select(item => item.Record.Name).ToList(),
                    Roles = Ordered(plan, rows),
                };
            }

            // ServiceSupervisor.Restart already stopped a role that started but did not get ready.
            Restore(launch.Record, previous, role);
            rows[role] = rows[role] with
            {
                Action = ServiceEnsureActions.StartFailed,
                Pid = null,
                Detail = launch.Failure ?? $"{role} did not get ready.",
            };
            Log.WriteLine($"Ensure could not start {role}: {launch.Failure}");
            foreach ((ServiceProcessRecord record, ServiceProcessRecord before) in started)
            {
                StopStarted(record, before);
                rows[record.Name] = rows[record.Name] with
                {
                    Action = ServiceEnsureActions.StartFailed,
                    Pid = null,
                    Detail = $"Stopped pid {record.Pid} again, because {role} did not start.",
                };
            }

            return plan with
            {
                Ok = false,
                Code = ServiceEnsureCodes.StartFailed,
                Message =
                    $"{role} did not start: {launch.Failure ?? "it did not get ready."} The roles this ensure started were stopped again; running roles were not touched.",
                Remediation =
                    $"Read the {role} log and `heroesreplay services status`, fix the cause, then run `heroesreplay services ensure` again.",
                SupervisorAttached = false,
                Started = Array.Empty<string>(),
                Roles = Ordered(plan, rows),
            };
        }

        string list = string.Join(
            ", ",
            started.Select(item => $"{item.Record.Name} pid {item.Record.Pid}")
        );
        return plan with
        {
            Message =
                $"Started {list}." + (plan.SupervisorAttached ? " Attaching a supervisor." : ""),
            Started = started.Select(item => item.Record.Name).ToList(),
            Roles = Ordered(plan, rows),
        };
    }

    /// <summary>The report as text: the verdict, one line per requested role, and the fix.</summary>
    public static void WriteText(TextWriter output, ServiceEnsureReport report)
    {
        output.WriteLine($"Services ensure: {report.Message} [{report.Code}]");
        foreach (ServiceEnsureRole role in report.Roles)
        {
            string pid = role.Pid is int value ? $"pid {value}" : string.Empty;
            string state = role.State.ToString().ToLowerInvariant();
            output.WriteLine($"  {role.Role, -9}{role.Action, -14}{state, -9}{pid}".TrimEnd());
            if (
                !string.IsNullOrWhiteSpace(role.Detail)
                && role.Action != ServiceEnsureActions.Running
            )
            {
                output.WriteLine($"{"", 11}{role.Detail}");
            }
        }

        if (!string.IsNullOrWhiteSpace(report.Remediation))
        {
            output.WriteLine($"Fix: {report.Remediation}");
        }
    }

    private static IReadOnlyList<ServiceEnsureRole> Ordered(
        ServiceEnsureReport plan,
        Dictionary<string, ServiceEnsureRole> rows
    ) => plan.Roles.Select(role => rows[role.Role]).ToList();

    private void StopStarted(ServiceProcessRecord record, ServiceProcessRecord previous)
    {
        try
        {
            Kill?.Invoke(record.Pid);
            Log.WriteLine($"Stopped {record.Name} pid {record.Pid} again.");
        }
        catch (Exception e)
        {
            Log.WriteLine($"Could not stop {record.Name} pid {record.Pid}: {e.Message}");
        }

        DeleteHeartbeat?.Invoke(record);
        Restore(record, previous, record.Name);
    }

    /// <summary>Puts <paramref name="record"/> in the lock in place of the role's last record.</summary>
    private void Record(ServiceProcessRecord record)
    {
        if (record == null)
        {
            return;
        }

        ServiceLock snapshot =
            ServiceLockStore.TryLoad(LockPath) ?? new ServiceLock { StartedAt = Time.GetUtcNow() };
        snapshot.Processes ??= new List<ServiceProcessRecord>();
        int index = snapshot.Processes.FindIndex(item =>
            string.Equals(item?.Name, record.Name, StringComparison.OrdinalIgnoreCase)
        );
        if (index >= 0)
        {
            snapshot.Processes[index] = record;
        }
        else
        {
            snapshot.Processes.Add(record);
        }

        ServiceLockStore.Save(LockPath, snapshot);
    }

    /// <summary>
    /// Undoes <see cref="Record"/> for a start that did not stay: the role's record before the
    /// ensure goes back, or the role leaves the lock when it had none.
    /// </summary>
    private void Restore(ServiceProcessRecord record, ServiceProcessRecord previous, string role)
    {
        ServiceLock snapshot = ServiceLockStore.TryLoad(LockPath);
        if (snapshot?.Processes == null)
        {
            return;
        }

        int index = snapshot.Processes.FindIndex(item =>
            string.Equals(item?.Name, role, StringComparison.OrdinalIgnoreCase)
            && (record == null || string.Equals(item.Nonce, record.Nonce, StringComparison.Ordinal))
        );
        if (index < 0)
        {
            return;
        }

        if (previous != null)
        {
            snapshot.Processes[index] = previous;
        }
        else
        {
            snapshot.Processes.RemoveAt(index);
        }

        if (snapshot.Processes.Count == 0)
        {
            ServiceLockStore.Delete(LockPath);
            return;
        }

        ServiceLockStore.Save(LockPath, snapshot);
    }

    private static ServiceProcessRecord Find(ServiceLock snapshot, string role) =>
        snapshot?.Processes?.FirstOrDefault(record =>
            record != null && string.Equals(record.Name, role, StringComparison.OrdinalIgnoreCase)
        );
}
