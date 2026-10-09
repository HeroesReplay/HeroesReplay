using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>
/// The opt-in supervisor loop (<c>services start --supervise</c>, <c>services supervise</c>).
/// Each pass classifies the recorded roles from the lock, the process table, and the heartbeats,
/// then applies <see cref="ServiceRestartPolicy"/>: a failed role restarts after its backoff
/// through the <c>services start</c> launch, a role stale past the limit is killed and then
/// restarts the same way, and a role with no budget left stays down with one error. A stop
/// request ends the loop before any restart. It never opens OBS or the game; restarting
/// spectate only closes a game the dead spectator left behind, and when spectate stays down for
/// good <see cref="SpectateDown"/> makes a live stream safe.
/// </summary>
public sealed class ServiceSupervision
{
    /// <summary>
    /// Supervisor log: a due restart found the role already running from this install, untracked
    /// (a child whose launcher gave up on it), and took that process over (#397).
    /// </summary>
    public const string RoleAdoptedCode = "service.role_adopted";

    /// <summary>
    /// Supervisor log: an untracked process of the role, with no heartbeat the supervisor can
    /// watch, was killed before the role restarted, so the role never runs twice (#397).
    /// </summary>
    public const string UntrackedRoleKilledCode = "service.untracked_role_killed";

    private readonly Dictionary<string, ServiceRoleRestarts> ledgers = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly List<string> supervised = new();
    private DateTimeOffset startedAt;
    private DateTimeOffset? processStartedAt;
    private DateTimeOffset? savedAt;
    private bool stopping;

    public string LockPath { get; init; } = ServiceLockStore.DefaultPath;
    public string StatePath { get; init; } = ServiceSupervisorFile.DefaultPath;
    public TimeProvider Time { get; init; } = TimeProvider.System;
    public ServiceRestartSettings Settings { get; init; } = new();
    public ServiceHealthSettings Health { get; init; } = new();
    public Func<int, string> ProcessNameOrNull { get; init; }
    public Func<int, ServiceProcessProbe> Probe { get; init; }
    public Func<ServiceProcessRecord, ServiceReadyReport> ReadHeartbeat { get; init; }

    /// <summary>Removes the ready file of a role that is gone, before it starts again.</summary>
    public Action<ServiceProcessRecord> DeleteHeartbeat { get; init; }

    /// <summary>True once <c>services stop</c> has its stop file down.</summary>
    public Func<bool> StopRequested { get; init; }

    /// <summary>
    /// Starts a role again. The request's callback sees the record as soon as the process has a
    /// pid, so a stop that arrives during the ready wait can still find it in the lock.
    /// </summary>
    public Func<ServiceLaunchRequest, ServiceLaunch> Launch { get; init; }
    public Action<int> Kill { get; init; }

    /// <summary>
    /// The live processes of a role that run this install's command but are not in
    /// <c>services.json</c>, each with the heartbeat it writes (#397). The second argument is
    /// every pid the lock tracks. A restart takes one over instead of starting a second process,
    /// and stops the ones it cannot watch. Null finds none.
    /// </summary>
    public Func<
        string,
        IReadOnlyCollection<int>,
        IReadOnlyList<UntrackedRoleProcess>
    > FindUntracked { get; init; }

    /// <summary>
    /// The commit charge, percent of the commit limit (<c>MachineHealth</c>). Above
    /// <see cref="ServiceRestartSettings.SlowReadyCommitPercent"/> a restart waits
    /// <see cref="ServiceRestartSettings.SlowReadyWait"/> for the role to get ready (#397). Null
    /// reads none.
    /// </summary>
    public Func<double?> CommitPercent { get; init; }

    /// <summary>Closes Heroes of the Storm before spectate starts again.</summary>
    public Func<bool> CloseGame { get; init; }

    /// <summary>
    /// Called once when spectate used its restart budget: makes a live OBS stream safe
    /// (<see cref="ObsFailSafe"/>) and returns what it did, for the log.
    /// </summary>
    public Func<string> SpectateDown { get; init; }

    /// <summary>The pause between passes. Production returns early on Ctrl+C.</summary>
    public Action<TimeSpan> Wait { get; init; }
    public ILogger Logger { get; init; } = NullLogger.Instance;
    public int Pid { get; init; } = Environment.ProcessId;

    /// <summary>The start time of <see cref="Pid"/>. Null reads it from the process table once.</summary>
    public DateTimeOffset? ProcessStartedAt { get; init; }
    public string ExecutablePath { get; init; }
    public string Version { get; init; }

    /// <summary>The supervisor's own log file, for <c>services status</c>.</summary>
    public Func<string> LogPath { get; init; }

    /// <summary>The hourly machine line (#251). Null logs none.</summary>
    public MachineHealthLog MachineHealth { get; init; }

    public IReadOnlyList<string> Supervised => supervised;

    public ServiceRoleRestarts Ledger(string role) =>
        ledgers.TryGetValue(role, out ServiceRoleRestarts ledger) ? ledger : null;

    /// <summary>Supervises until a stop request, Ctrl+C, or the lock is gone. Exit code 1 when nothing is recorded.</summary>
    public int Run(CancellationToken cancellation)
    {
        try
        {
            if (!Begin())
            {
                return 1;
            }

            while (!cancellation.IsCancellationRequested)
            {
                if (!Tick())
                {
                    return 0;
                }

                (Wait ?? Thread.Sleep).Invoke(Settings.Poll);
            }

            Logger.LogWarning(
                "The supervisor was interrupted. The roles keep running without restarts; `heroesreplay services supervise` resumes."
            );
            return 0;
        }
        finally
        {
            ServiceSupervisorFile.Delete(StatePath);
        }
    }

    /// <summary>Picks the recorded roles to watch. False when <c>services.json</c> records none.</summary>
    public bool Begin()
    {
        startedAt = Time.GetUtcNow();
        // A session that cannot see the mutex (SSH) tells this pid from a reused one by it (#283).
        processStartedAt = ProcessStartedAt ?? ProcessTable.Find(Pid)?.StartTime;
        ServiceLock snapshot = ServiceLockStore.TryLoad(LockPath);
        supervised.Clear();
        ledgers.Clear();
        foreach (string role in ServiceProcessPlan.Names)
        {
            ServiceProcessRecord record = Find(snapshot, role);
            if (record == null)
            {
                continue;
            }

            supervised.Add(role);
            ledgers[role] = new ServiceRoleRestarts { Role = role, Nonce = record.Nonce };
        }

        if (supervised.Count == 0)
        {
            Logger.LogError(
                "No roles are recorded in services.json, so there is nothing to supervise. Run `heroesreplay services start --supervise`."
            );
            return false;
        }

        Logger.LogInformation(
            "Supervising {Roles}. Backoff {Backoff}; budget {Budget} restarts per {Window}; a role whose heartbeat is {Stale} old is killed and restarted. `heroesreplay services stop` stops the roles and this supervisor.",
            string.Join(", ", supervised),
            string.Join(", ", Settings.Delays.Select(ServiceHealthClassifier.Describe)),
            Settings.Limit,
            ServiceHealthClassifier.Describe(Settings.Window),
            ServiceHealthClassifier.Describe(Settings.StaleLimit)
        );
        Save(force: true);
        return true;
    }

    /// <summary>One pass. False when the supervisor should exit: a stop request, or no lock.</summary>
    public bool Tick()
    {
        if (Stopping())
        {
            return false;
        }

        ServiceLock snapshot = ServiceLockStore.TryLoad(LockPath);
        if (snapshot?.Processes == null || snapshot.Processes.Count == 0)
        {
            Logger.LogWarning(
                "services.json is gone, so there is nothing left to supervise. The supervisor exits."
            );
            return false;
        }

        DateTimeOffset now = Time.GetUtcNow();
        ServiceStatusReport report = ServiceHealthClassifier.Build(
            snapshot,
            ProcessNameOrNull,
            Probe,
            ReadHeartbeat,
            stopRequested: false,
            now,
            Health
        );
        bool changed = false;
        foreach (string role in supervised)
        {
            ServiceProcessRecord record = Find(snapshot, role);
            ServiceRoleHealth health = report.Roles.FirstOrDefault(item =>
                string.Equals(item.Role, role, StringComparison.OrdinalIgnoreCase)
            );
            if (record == null || health == null)
            {
                continue;
            }

            ServiceRoleRestarts ledger = ledgers[role];
            switch (ServiceRestartPolicy.Decide(health, ledger, now, Settings))
            {
                case ServiceRestartAction.Scheduled:
                    changed = true;
                    Logger.LogWarning(
                        "{Role} is down: {Cause} Restart {Attempt} of {Budget} in {Delay}.",
                        role,
                        health.Cause,
                        ledger.Recent.Count + 1,
                        Settings.Limit,
                        ServiceHealthClassifier.Describe((ledger.NextRestartAt ?? now) - now)
                    );
                    break;
                case ServiceRestartAction.Kill:
                    changed = true;
                    KillStale(role, record, health);
                    break;
                case ServiceRestartAction.Exhausted:
                    changed = true;
                    Logger.LogError(
                        "{Role} used its restart budget ({Budget} restarts in {Window}) and stays down [{Code}]. {Cause} Fix the cause, then run `heroesreplay services stop` and `heroesreplay services start --supervise`.",
                        role,
                        Settings.Limit,
                        ServiceHealthClassifier.Describe(Settings.Window),
                        ServiceHealthCodes.RestartBudgetExhausted,
                        health.Cause
                    );
                    if (string.Equals(role, "spectate", StringComparison.OrdinalIgnoreCase))
                    {
                        MakeObsSafe();
                    }

                    break;
                case ServiceRestartAction.Restart:
                    if (Stopping())
                    {
                        return false;
                    }

                    changed = true;
                    if (!Restart(role, record, ledger))
                    {
                        return false;
                    }

                    break;
            }
        }

        Save(changed);
        MachineHealth?.Tick(now);
        return true;
    }

    /// <summary>
    /// Spectate is down for good, so nothing drives OBS: apply
    /// <see cref="ServiceRestartSettings.SpectateDownObs"/> to a live stream through
    /// <see cref="SpectateDown"/>.
    /// </summary>
    private void MakeObsSafe()
    {
        if (SpectateDown == null)
        {
            return;
        }

        try
        {
            Logger.LogWarning("Spectate is down for good. {Obs}", SpectateDown());
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Spectate is down for good, and OBS was not made safe.");
        }
    }

    private bool Restart(string role, ServiceProcessRecord previous, ServiceRoleRestarts ledger)
    {
        DateTimeOffset at = Time.GetUtcNow();
        // A process of this role may already run untracked: a child whose launcher gave up on
        // it, as on the stream PC on 2026-10-09 (#397). Take it over instead of starting a second.
        if (AdoptUntracked(role, ledger, at))
        {
            return true;
        }

        Logger.LogInformation(
            "Restarting {Role} ({Reason}): restart {Attempt} of {Budget} in {Window}.",
            role,
            ledger.DownReason ?? ServiceRestartPolicy.FailedReason,
            ledger.Recent.Count + 1,
            Settings.Limit,
            ServiceHealthClassifier.Describe(Settings.Window)
        );
        if (string.Equals(role, "spectate", StringComparison.OrdinalIgnoreCase))
        {
            CloseLeftoverGame();
        }

        DeleteHeartbeat?.Invoke(previous);
        TimeSpan readyWait = ReadyWait(role);
        // The ready wait can take minutes. Written now and on each pause of the wait, the file
        // stays fresh through it for a session that cannot see the mutex
        // (ServiceSupervisorFile.FreshFor).
        Save(force: true);
        ServiceLaunch launch;
        try
        {
            launch =
                Launch?.Invoke(
                    new ServiceLaunchRequest(role, readyWait, Record, () => Save(force: false))
                )
                ?? new ServiceLaunch(null, false, false, "No launcher was given.");
        }
        catch (Exception e)
        {
            launch = new ServiceLaunch(null, false, false, $"{role} failed: {e.Message}");
        }

        if (launch.Cancelled)
        {
            Logger.LogInformation(
                "A stop was requested while {Role} pid {Pid} was starting. `heroesreplay services stop` stops it.",
                role,
                launch.Record?.Pid
            );
            return false;
        }

        if (launch.Ready && launch.Record != null)
        {
            Record(launch.Record);
            ServiceRestartPolicy.Restarted(ledger, at, launch.Record.Nonce, null);
            Logger.LogInformation(
                "Restarted {Role} as pid {Pid}: {Used} of {Budget} restarts used in {Window}.",
                role,
                launch.Record.Pid,
                ledger.Recent.Count,
                Settings.Limit,
                ServiceHealthClassifier.Describe(Settings.Window)
            );
        }
        else
        {
            string failure = string.IsNullOrWhiteSpace(launch.Failure)
                ? $"{role} did not get ready."
                : launch.Failure;
            ServiceRestartPolicy.Restarted(ledger, at, launch.Record?.Nonce, failure);
            if (launch.StillRunning && launch.Record != null)
            {
                // It would not stop: services.json keeps it, so it is never left unsupervised.
                Record(launch.Record);
                Logger.LogWarning(
                    "Restart of {Role} failed: {Failure} Pid {Pid} could not be stopped, so services.json keeps it and the supervisor watches it like any role. It counts against the budget.",
                    role,
                    failure,
                    launch.Record.Pid
                );
            }
            else
            {
                // services.json keeps the role without a pid, never a dead one (#397).
                RecordDown(role, previous, ledger.Nonce);
                Logger.LogWarning(
                    "Restart of {Role} failed: {Failure} No process of it was left running. It counts against the budget.",
                    role,
                    failure
                );
            }
        }

        return true;
    }

    /// <summary>
    /// Before a restart starts <paramref name="role"/>: a live process of it from this install
    /// that <c>services.json</c> does not track is taken over when it writes a heartbeat the
    /// supervisor can watch, and killed when it does not, so the role never runs twice and no
    /// role process runs unsupervised (#397). True when one was taken over; nothing is started
    /// then, so no budget is used.
    /// </summary>
    private bool AdoptUntracked(string role, ServiceRoleRestarts ledger, DateTimeOffset at)
    {
        if (FindUntracked == null)
        {
            return false;
        }

        IReadOnlyList<UntrackedRoleProcess> found;
        try
        {
            found = FindUntracked(role, TrackedPids()) ?? Array.Empty<UntrackedRoleProcess>();
        }
        catch (Exception e)
        {
            Logger.LogWarning(
                "Could not look for a {Role} process services.json does not track: {Message}",
                role,
                e.Message
            );
            return false;
        }

        UntrackedRoleProcess adopted = found
            .Where(item => item?.Process != null && item.Adoptable)
            .OrderByDescending(item => item.Heartbeat.HeartbeatAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
        foreach (UntrackedRoleProcess other in found)
        {
            if (other?.Process == null || ReferenceEquals(other, adopted))
            {
                continue;
            }

            Logger.LogWarning(
                "{Role} pid {Pid} runs this install's `{Arguments}` (started {StartedAt:O}) but services.json does not track it, and {Why}. Killing it before the restart, so the role never runs twice [{Code}].",
                role,
                other.Process.Pid,
                other.Process.Arguments,
                other.Process.StartedAt,
                adopted == null
                    ? "it writes no heartbeat the supervisor can watch"
                    : $"pid {adopted.Process.Pid} is the one taken over",
                UntrackedRoleKilledCode
            );
            KillUntracked(role, other.Process.Pid);
        }

        if (adopted == null)
        {
            return false;
        }

        ServiceProcessRecord record = adopted.Adopt();
        Record(record);
        ServiceRestartPolicy.Adopted(ledger, at, record.Nonce, record.Pid);
        string age = adopted.Heartbeat.HeartbeatAt is DateTimeOffset beat
            ? ServiceHealthClassifier.Describe(at - beat) + " ago"
            : "never";
        Logger.LogWarning(
            "{Role} pid {Pid} already runs this install's `{Arguments}` (started {StartedAt:O}, last heartbeat {Age}), but services.json did not track it. Took it over instead of starting a second process [{Code}]. No restart budget was used.",
            role,
            record.Pid,
            record.Arguments,
            record.StartedAt,
            age,
            RoleAdoptedCode
        );
        return true;
    }

    private void KillUntracked(string role, int pid)
    {
        try
        {
            if (Kill == null)
            {
                throw new InvalidOperationException("No kill step was given.");
            }

            Kill(pid);
        }
        catch (Exception e)
        {
            Logger.LogWarning("Could not kill {Role} pid {Pid}: {Message}", role, pid, e.Message);
        }
    }

    /// <summary>Every pid the lock tracks, and this supervisor's own.</summary>
    private IReadOnlyCollection<int> TrackedPids()
    {
        var pids = new HashSet<int> { Pid };
        foreach (
            ServiceProcessRecord record in ServiceLockStore.TryLoad(LockPath)?.Processes
                ?? new List<ServiceProcessRecord>()
        )
        {
            if (record?.Pid > 0)
            {
                pids.Add(record.Pid);
            }
        }

        return pids;
    }

    /// <summary>
    /// The ready wait for this restart: <see cref="ServiceRestartSettings.ReadyWait"/>, or the
    /// longer <see cref="ServiceRestartSettings.SlowReadyWait"/> while the commit charge is above
    /// <see cref="ServiceRestartSettings.SlowReadyCommitPercent"/>, logged (#397).
    /// </summary>
    private TimeSpan ReadyWait(string role)
    {
        double? commit = null;
        try
        {
            commit = CommitPercent?.Invoke();
        }
        catch (Exception e)
        {
            Logger.LogWarning("Could not read the commit charge: {Message}", e.Message);
        }

        TimeSpan wait = Settings.ReadyWaitFor(commit);
        if (wait > Settings.ReadyWait)
        {
            Logger.LogWarning(
                "The commit charge is {Commit:0.#}% of the limit, above {Limit:0.#}%, so this restart waits up to {Wait} for {Role} to get ready instead of {Normal}. A slow start on a machine short of memory is not a failed one.",
                commit,
                Settings.SlowReadyCommitPercent,
                ServiceHealthClassifier.Describe(wait),
                role,
                ServiceHealthClassifier.Describe(Settings.ReadyWait)
            );
        }

        return wait;
    }

    /// <summary>
    /// After a restart that left no process: the role stays in the lock without a pid, so
    /// <c>services.json</c> never names a dead process as the role's (#397), and the role is
    /// still expected, failed, and restarted after its backoff.
    /// </summary>
    private void RecordDown(string role, ServiceProcessRecord previous, string nonce) =>
        Record(
            new ServiceProcessRecord
            {
                Name = previous?.Name ?? role,
                Pid = 0,
                Arguments = previous?.Arguments ?? ServiceProcessPlan.ArgumentsFor(role),
                ExecutablePath = previous?.ExecutablePath ?? ExecutablePath,
                Nonce = nonce,
                Version = previous?.Version ?? Version,
            }
        );

    private void KillStale(string role, ServiceProcessRecord record, ServiceRoleHealth health)
    {
        if (health.CauseCode == ServiceHealthCodes.SpectateLaunchStalled)
        {
            Logger.LogWarning(
                "{Role} made no match progress while launching [{Code}]: {Cause} Killing pid {Pid}; it restarts after its backoff.",
                role,
                health.CauseCode,
                health.Cause,
                record.Pid
            );
        }
        else
        {
            Logger.LogWarning(
                "{Role} is stale: {Cause} Killing pid {Pid}; it restarts after its backoff.",
                role,
                health.Cause,
                record.Pid
            );
        }

        try
        {
            if (Kill == null)
            {
                throw new InvalidOperationException("No kill step was given.");
            }

            Kill(record.Pid);
        }
        catch (Exception e)
        {
            Logger.LogWarning(
                "Could not kill {Role} pid {Pid}: {Message}",
                role,
                record.Pid,
                e.Message
            );
        }
    }

    private void CloseLeftoverGame()
    {
        try
        {
            if (CloseGame?.Invoke() == false)
            {
                Logger.LogWarning("Heroes of the Storm is still running before spectate restarts.");
            }
        }
        catch (Exception e)
        {
            Logger.LogWarning("Could not close Heroes of the Storm: {Message}", e.Message);
        }
    }

    /// <summary>Puts <paramref name="record"/> in the lock in place of the role's last record.</summary>
    private void Record(ServiceProcessRecord record)
    {
        if (record == null)
        {
            return;
        }

        try
        {
            ServiceLock snapshot =
                ServiceLockStore.TryLoad(LockPath)
                ?? new ServiceLock { StartedAt = Time.GetUtcNow() };
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
        catch (IOException e)
        {
            Logger.LogWarning(
                "Could not record {Role} pid {Pid} in services.json: {Message}",
                record.Name,
                record.Pid,
                e.Message
            );
        }
    }

    private bool Stopping()
    {
        if (StopRequested?.Invoke() != true)
        {
            return false;
        }

        if (!stopping)
        {
            stopping = true;
            Logger.LogInformation(
                "A stop was requested (services.stop). The supervisor restarts nothing more and exits."
            );
            Save(force: true);
        }

        return true;
    }

    // The state is small. It is written when a role changes, and at least every heartbeat interval.
    private void Save(bool force)
    {
        DateTimeOffset now = Time.GetUtcNow();
        if (!force && savedAt is DateTimeOffset last && now - last < Health.Interval)
        {
            return;
        }

        try
        {
            ServiceSupervisorFile.Save(
                StatePath,
                new ServiceSupervisorState
                {
                    Pid = Pid,
                    ProcessStartedAt = processStartedAt,
                    ExecutablePath = ExecutablePath,
                    Version = Version,
                    StartedAt = startedAt,
                    UpdatedAt = now,
                    Stopping = stopping,
                    LogPath = LogPath?.Invoke(),
                    BackoffSeconds = Settings
                        .Delays.Select(delay => (long)delay.TotalSeconds)
                        .ToList(),
                    Budget = Settings.Limit,
                    BudgetWindowSeconds = (long)Settings.Window.TotalSeconds,
                    StaleRestartAfterSeconds = (long)Settings.StaleLimit.TotalSeconds,
                    Supervised = supervised.ToList(),
                    Roles = supervised.Select(role => ledgers[role]).ToList(),
                }
            );
            savedAt = now;
        }
        catch (IOException e)
        {
            Logger.LogWarning("Could not write supervisor.json: {Message}", e.Message);
        }
        catch (UnauthorizedAccessException e)
        {
            Logger.LogWarning("Could not write supervisor.json: {Message}", e.Message);
        }
    }

    /// <summary>
    /// The role's record. One without a pid is a role whose last restart left no process
    /// (<see cref="RecordDown"/>): still supervised, and failed.
    /// </summary>
    private static ServiceProcessRecord Find(ServiceLock snapshot, string role) =>
        snapshot?.Processes?.FirstOrDefault(record =>
            record != null
            && record.Pid >= 0
            && string.Equals(record.Name, role, StringComparison.OrdinalIgnoreCase)
        );
}
