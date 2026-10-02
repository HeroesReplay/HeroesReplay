using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Status;

namespace HeroesReplay.CLI.Commands.Services;

public static class ServiceSupervisor
{
    public static int Start(
        string lockPath,
        string exePath,
        Func<int, string> processNameOrNull,
        Func<string, string, int?> startProcess,
        Action clearStopFile = null,
        Action ensureDashboard = null,
        ServiceStartupHandshake handshake = null
    )
    {
        if (
            string.IsNullOrWhiteSpace(exePath)
            || !ServiceProcessPlan.IsHeroesReplay(Path.GetFileName(exePath))
        )
        {
            Console.Error.WriteLine($"Refusing to start services from `{exePath}`.");
            return 1;
        }

        handshake ??= new ServiceStartupHandshake();
        if (handshake.TryReadReady == null)
        {
            handshake.TryReadReady = record => ServiceReadyFile.TryRead(record);
        }

        List<ServiceProcessRecord> living = ServiceProcessPlan.StillRunning(
            ServiceLockStore.TryLoad(lockPath)?.Processes,
            processNameOrNull,
            handshake.Probe
        );
        if (living.Count > 0)
        {
            Console.WriteLine("Services already running. Run `heroesreplay services stop` first.");
            foreach (ServiceProcessRecord record in living)
            {
                Console.WriteLine($"  {record.Name} pid {record.Pid} ({record.Arguments})");
            }

            return 1;
        }

        clearStopFile?.Invoke();
        try
        {
            ensureDashboard?.Invoke();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(
                $"Aspire dashboard was not started. Continuing without the dashboard. {e.Message}"
            );
        }

        string version = string.IsNullOrWhiteSpace(handshake.Version)
            ? CurrentVersion()
            : handshake.Version;
        var started = new List<ServiceProcessRecord>();
        foreach ((string name, string arguments) in ServiceProcessPlan.All)
        {
            try
            {
                string validation = Validate(name, exePath, handshake);
                if (validation != null)
                {
                    Fail(handshake, $"{name} failed: {validation}");
                    Rollback(lockPath, started, handshake);
                    return 1;
                }

                var record = new ServiceProcessRecord
                {
                    Name = name,
                    Arguments = arguments,
                    ExecutablePath = exePath,
                    Nonce = Guid.NewGuid().ToString("N"),
                    Version = version,
                };
                handshake.Pending = record;
                int? pid = startProcess(name, arguments);
                if (pid is not int id || id <= 0)
                {
                    Fail(handshake, $"Failed to start {name} ({arguments}).");
                    Rollback(lockPath, started, handshake);
                    return 1;
                }

                record.Pid = id;
                started.Add(record);
                ServiceProcessProbe probed = handshake.Probe?.Invoke(id);
                if (!string.IsNullOrWhiteSpace(probed?.ExecutablePath))
                {
                    record.ExecutablePath = probed.ExecutablePath;
                }

                record.StartedAt = probed?.StartedAt ?? DateTimeOffset.UtcNow;
                Console.WriteLine($"Started {name} pid {id} ({arguments}).");
                if (!WaitForReady(record, processNameOrNull, handshake))
                {
                    Rollback(lockPath, started, handshake);
                    return 1;
                }
            }
            catch (Exception e)
            {
                Fail(handshake, $"{name} failed: {e.Message}");
                Rollback(lockPath, started, handshake);
                return 1;
            }
        }

        if (started.Count != ServiceProcessPlan.All.Count)
        {
            Fail(handshake, "Service startup failed before every role was ready.");
            Rollback(lockPath, started, handshake);
            return 1;
        }

        ServiceLockStore.Save(
            lockPath,
            new ServiceLock { StartedAt = DateTimeOffset.UtcNow, Processes = started }
        );
        Console.WriteLine(TwitchIngestGuard.NotStartedMessage);
        Console.WriteLine(
            $"OpenTelemetry export: {AspireDashboardHost.OtlpGrpcEndpoint} (Aspire dashboard {AspireDashboardHost.UiUrl})."
        );
        return 0;
    }

    /// <summary>
    /// Ask every recorded role to exit, kill the ones left after the graceful budget, close the
    /// game, then read the OBS stream state. Exit code 0 needs all three confirmed. A role that is
    /// still running stays in the lock so status and a second stop can still find it.
    /// </summary>
    public static ServiceStopResult Stop(string lockPath, ServiceShutdown shutdown)
    {
        ArgumentNullException.ThrowIfNull(shutdown);
        ArgumentNullException.ThrowIfNull(shutdown.ProcessNameOrNull);
        ServiceLock snapshot = ServiceLockStore.TryLoad(lockPath);
        List<ServiceProcessRecord> recorded = (
            snapshot?.Processes ?? new List<ServiceProcessRecord>()
        )
            .Where(record => record != null && record.Pid > 0)
            .ToList();
        bool hadSpectate = recorded.Any(record => record.Name == "spectate");
        Action<TimeSpan> pause = shutdown.Wait ?? Thread.Sleep;

        // Until the processes are probed, every recorded role counts as running.
        List<ServiceProcessRecord> survivors = recorded;
        try
        {
            // Probe before asking, so a role that exits at once is graceful, not already gone.
            List<ServiceProcessRecord> living = StillRunning(recorded, shutdown);
            var asked = new HashSet<ServiceProcessRecord>(living);
            shutdown.RequestGracefulStop?.Invoke();
            RequestStreamShutdown(shutdown.ConfirmStream);

            // The budget ends on the wall clock or on the summed pauses, whichever is first.
            TimeSpan interval = TimeSpan.FromMilliseconds(200);
            TimeSpan waited = TimeSpan.Zero;
            DateTimeOffset until = DateTimeOffset.UtcNow + shutdown.GracefulWait;
            while (
                living.Count > 0 && waited < shutdown.GracefulWait && DateTimeOffset.UtcNow < until
            )
            {
                pause(interval);
                waited += interval;
                living = StillRunning(living, shutdown);
            }

            var killed = new HashSet<ServiceProcessRecord>();
            var killErrors = new Dictionary<ServiceProcessRecord, string>();
            foreach (ServiceProcessRecord record in living)
            {
                try
                {
                    if (shutdown.Kill == null)
                    {
                        throw new InvalidOperationException("No kill step was given.");
                    }

                    shutdown.Kill(record.Pid);
                    killed.Add(record);
                }
                catch (Exception e)
                {
                    killErrors[record] = "Kill failed: " + e.Message;
                }
            }

            survivors = StillRunning(living, shutdown);
            var roles = new List<ServiceRoleStop>();
            foreach (ServiceProcessRecord record in recorded)
            {
                ServiceStopOutcome outcome;
                if (!asked.Contains(record))
                {
                    outcome = ServiceStopOutcome.AlreadyExited;
                }
                else if (survivors.Contains(record))
                {
                    outcome = ServiceStopOutcome.StillRunning;
                }
                else if (killed.Contains(record))
                {
                    outcome = ServiceStopOutcome.Killed;
                }
                else
                {
                    // It left during the budget, or on its own before the kill reached it.
                    outcome = ServiceStopOutcome.Graceful;
                }

                killErrors.TryGetValue(record, out string detail);
                var role = new ServiceRoleStop(
                    record.Name,
                    record.Pid,
                    outcome,
                    outcome == ServiceStopOutcome.StillRunning ? detail : null
                );
                roles.Add(role);
                Console.WriteLine("  " + role.Describe());
            }

            bool? gameClosed = hadSpectate ? CloseGame(shutdown.CloseGame) : null;
            if (gameClosed is bool closed)
            {
                Console.WriteLine(
                    closed ? "Heroes of the Storm: closed." : "Heroes of the Storm: still running."
                );
            }

            ServiceStreamCheck stream = null;
            if (survivors.Count > 0)
            {
                // The spectator may still hold its OBS session. Do not open a second websocket.
                Console.WriteLine("OBS stream: not read, because a role is still running.");
            }
            else if (shutdown.ReadStream != null)
            {
                stream = ReadStream(shutdown.ReadStream);
                Console.WriteLine("OBS stream: " + stream.Describe());
            }

            var result = new ServiceStopResult
            {
                Roles = roles,
                GameClosed = gameClosed,
                Stream = stream,
            };
            if (killed.Count > 0)
            {
                Console.WriteLine(
                    "Forced stop skipped the shutdown of the killed roles. The game and the OBS stream were checked separately."
                );
            }

            if (result.Succeeded)
            {
                Console.WriteLine(
                    recorded.Count == 0
                        ? "No HeroesReplay services are running."
                        : "Services stopped."
                );
            }
            else
            {
                Console.Error.WriteLine(
                    "Services did not stop cleanly. " + string.Join(" ", result.Failures())
                );
            }

            return result;
        }
        finally
        {
            foreach (ServiceProcessRecord record in recorded)
            {
                if (!survivors.Contains(record))
                {
                    ServiceReadyFile.Delete(record.Nonce);
                }
            }

            if (survivors.Count > 0)
            {
                ServiceLockStore.Save(
                    lockPath,
                    new ServiceLock
                    {
                        StartedAt = snapshot?.StartedAt ?? DateTimeOffset.UtcNow,
                        Processes = survivors.ToList(),
                    }
                );
            }
            else
            {
                ServiceLockStore.Delete(lockPath);
            }

            shutdown.ClearStopFile?.Invoke();
        }
    }

    private static List<ServiceProcessRecord> StillRunning(
        IEnumerable<ServiceProcessRecord> records,
        ServiceShutdown shutdown
    ) => ServiceProcessPlan.StillRunning(records, shutdown.ProcessNameOrNull, shutdown.Probe);

    private static bool? CloseGame(Func<bool> closeGame)
    {
        if (closeGame == null)
        {
            return null;
        }

        try
        {
            return closeGame();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("Could not close Heroes of the Storm. " + e.Message);
            return false;
        }
    }

    private static ServiceStreamCheck ReadStream(Func<ServiceStreamCheck> readStream)
    {
        try
        {
            return readStream()
                ?? ServiceStreamCheck.Unknown("The OBS stream reader returned nothing.");
        }
        catch (Exception e)
        {
            return ServiceStreamCheck.Unknown(e.Message);
        }
    }

    public static int Status(
        string lockPath,
        Func<int, string> processNameOrNull,
        SpectatorStatus spectator,
        Func<int, ServiceProcessProbe> probeOrNull = null
    )
    {
        ServiceLock snapshot = ServiceLockStore.TryLoad(lockPath);
        int expected = snapshot?.Processes?.Count ?? 0;
        List<ServiceProcessRecord> living = ServiceProcessPlan.StillRunning(
            snapshot?.Processes,
            processNameOrNull,
            probeOrNull
        );
        if (expected == 0)
        {
            Console.WriteLine("Services: not running.");
        }
        else
        {
            Console.WriteLine($"Services: {living.Count} of {expected} still running.");
            foreach (ServiceProcessRecord record in living)
            {
                Console.WriteLine($"  {record.Name} pid {record.Pid} ({record.Arguments})");
            }
        }

        if (spectator == null)
        {
            Console.WriteLine("Spectator status: no snapshot.");
        }
        else
        {
            string obs = ObsStatus.Describe(spectator);
            Console.WriteLine(
                $"Spectator status: phase={spectator.Phase} running={spectator.SpectatorRunning} stale={spectator.SnapshotStale} replay={spectator.ReplayId} map={spectator.Map} timer={spectator.Timer}"
                    + (obs == null ? "" : " " + obs)
            );
            if (spectator.CompletedReplayId.HasValue)
            {
                Console.WriteLine(
                    $"Last completion: replay {spectator.CompletedReplayId} team {spectator.CompletedWinnerTeam} at {spectator.CompletedAt:O}"
                );
            }
        }

        if (expected == 0)
        {
            return 0;
        }

        return living.Count == expected ? 0 : 1;
    }

    private static void RequestStreamShutdown(Func<ObsShutdownPlan, ObsStreamResult> confirmStream)
    {
        ObsShutdownPlan plan = ObsServiceStop.PlanForServicesCommand();
        if (plan.CloseProcess)
        {
            Console.Error.WriteLine(
                "Refusing to close OBS. This command does not own the process."
            );
            return;
        }

        if (confirmStream == null)
        {
            return;
        }

        try
        {
            ObsStreamResult stream = confirmStream(plan);
            if (
                stream != null
                && !stream.Succeeded
                && stream.Failure != ObsOutputFailure.NotRequested
            )
            {
                Console.Error.WriteLine(
                    "OBS stream was not confirmed inactive ("
                        + stream.Failure
                        + "). "
                        + stream.Detail
                );
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("OBS stream was not confirmed inactive. " + e.Message);
        }
    }

    private static string Validate(string role, string exePath, ServiceStartupHandshake handshake)
    {
        switch (role)
        {
            case "spectate":
                return ServiceRoleChecks.SpectateFailure(exePath, handshake.Spectate);
            case "twitch":
                return ServiceRoleChecks.TwitchFailure(handshake.Twitch);
            case "download":
                return ServiceRoleChecks.DownloadFailure(handshake.Download);
            case "youtube":
                return ServiceRoleChecks.YouTubeFailure(handshake.YouTube);
            default:
                return "unknown service role.";
        }
    }

    private static bool WaitForReady(
        ServiceProcessRecord record,
        Func<int, string> processNameOrNull,
        ServiceStartupHandshake handshake
    )
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + handshake.ReadyTimeout;
        while (true)
        {
            ServiceReadyReport report = handshake.TryReadReady(record);
            bool exited = !ServiceProcessPlan.IsHeroesReplay(processNameOrNull(record.Pid));
            // A ready report wins over a process-name miss so tests can report ready without a live PID.
            if (IsReady(record, report))
            {
                if (ServiceChildHeartbeat.ExitCode(report, exited) == 1)
                {
                    Fail(handshake, $"{record.Name} failed: exited before heartbeat.");
                    return false;
                }

                if (ServiceChildHeartbeat.FollowsReady(report))
                {
                    record.ReadyAt = report.ReadyAt ?? DateTimeOffset.UtcNow;
                    record.HeartbeatAt = report.HeartbeatAt;
                    if (!string.IsNullOrWhiteSpace(report.Version))
                    {
                        record.Version = report.Version;
                    }

                    return true;
                }
            }
            else if (exited)
            {
                Fail(handshake, $"{record.Name} failed: exited before ready.");
                return false;
            }

            if (handshake.ReadyTimeout <= TimeSpan.Zero || DateTimeOffset.UtcNow >= deadline)
            {
                Fail(
                    handshake,
                    IsReady(record, report)
                        ? $"{record.Name} failed: heartbeat timed out."
                        : $"{record.Name} failed: ready timed out."
                );
                return false;
            }

            TimeSpan pause =
                handshake.PollInterval > TimeSpan.Zero
                    ? handshake.PollInterval
                    : TimeSpan.FromMilliseconds(200);
            (handshake.Wait ?? Thread.Sleep).Invoke(pause);
        }
    }

    private static bool IsReady(ServiceProcessRecord record, ServiceReadyReport report)
    {
        return report != null
            && !string.IsNullOrWhiteSpace(report.Nonce)
            && string.Equals(report.Nonce, record.Nonce, StringComparison.Ordinal)
            && (
                string.IsNullOrWhiteSpace(report.Role)
                || string.Equals(report.Role, record.Name, StringComparison.OrdinalIgnoreCase)
            );
    }

    private static void Rollback(
        string lockPath,
        IReadOnlyList<ServiceProcessRecord> started,
        ServiceStartupHandshake handshake
    )
    {
        if (started != null && handshake?.StopStarted != null)
        {
            for (int index = started.Count - 1; index >= 0; index--)
            {
                ServiceProcessRecord record = started[index];
                if (record == null || record.Pid <= 0)
                {
                    continue;
                }

                try
                {
                    handshake.StopStarted(record.Pid);
                    Console.WriteLine($"Rolled back {record.Name} pid {record.Pid}.");
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(
                        $"Could not stop {record.Name} pid {record.Pid}: {e.Message}"
                    );
                }
            }
        }

        if (started != null)
        {
            foreach (ServiceProcessRecord record in started)
            {
                ServiceReadyFile.Delete(record?.Nonce);
            }
        }

        ServiceLockStore.Delete(lockPath);
    }

    private static void Fail(ServiceStartupHandshake handshake, string message)
    {
        Console.Error.WriteLine(message);
        handshake?.Report?.Invoke(message);
    }

    private static string CurrentVersion()
    {
        Assembly assembly = typeof(ServiceSupervisor).Assembly;
        string informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
