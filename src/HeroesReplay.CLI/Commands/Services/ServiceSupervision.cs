using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.ServiceHost;
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
    private readonly Dictionary<string, ServiceRoleRestarts> ledgers = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly List<string> supervised = new();
    private DateTimeOffset startedAt;
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
    /// Starts a role again. The callback sees the record as soon as the process has a pid, so a
    /// stop that arrives during the ready wait can still find it in the lock.
    /// </summary>
    public Func<string, Action<ServiceProcessRecord>, ServiceLaunch> Launch { get; init; }
    public Action<int> Kill { get; init; }

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
            Logger.LogWarning(
                "Restart of {Role} failed: {Failure} It counts against the budget.",
                role,
                failure
            );
        }

        return true;
    }

    private void KillStale(string role, ServiceProcessRecord record, ServiceRoleHealth health)
    {
        Logger.LogWarning(
            "{Role} is stale: {Cause} Killing pid {Pid}; it restarts after its backoff.",
            role,
            health.Cause,
            record.Pid
        );
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

    private static ServiceProcessRecord Find(ServiceLock snapshot, string role) =>
        snapshot?.Processes?.FirstOrDefault(record =>
            record != null
            && record.Pid > 0
            && string.Equals(record.Name, role, StringComparison.OrdinalIgnoreCase)
        );
}
