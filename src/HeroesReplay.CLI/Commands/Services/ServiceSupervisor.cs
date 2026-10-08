using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.ServiceHost.Logs;
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
        ServiceStartupHandshake handshake = null,
        IReadOnlyCollection<string> roles = null
    )
    {
        if (!IsHeroesReplayExe(exePath))
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
        IReadOnlyList<(string Name, string Arguments)> plan = ServiceProcessPlan.Select(roles);
        var started = new List<ServiceProcessRecord>();
        foreach ((string name, _) in plan)
        {
            ServiceLaunch launch = Launch(
                name,
                exePath,
                version,
                startProcess,
                processNameOrNull,
                handshake
            );
            if (launch.Record != null)
            {
                started.Add(launch.Record);
            }

            if (!launch.Ready)
            {
                Rollback(lockPath, started, handshake);
                return 1;
            }
        }

        if (started.Count != plan.Count)
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
    /// Starts one role again for the supervisor, through the same launch as <c>services start</c>:
    /// the role's prerequisites, a new nonce, the same arguments, and the ready handshake. A role
    /// that started but did not get ready is stopped again, unless a stop request cut the wait
    /// short; then <c>services stop</c> owns it through <paramref name="started"/>.
    /// </summary>
    public static ServiceLaunch Restart(
        string name,
        string exePath,
        Func<string, string, int?> startProcess,
        Func<int, string> processNameOrNull,
        ServiceStartupHandshake handshake,
        Action<ServiceProcessRecord> started = null
    )
    {
        if (!IsHeroesReplayExe(exePath))
        {
            return new ServiceLaunch(
                null,
                false,
                false,
                Fail(handshake, $"Refusing to start {name} from `{exePath}`.")
            );
        }

        handshake ??= new ServiceStartupHandshake();
        handshake.TryReadReady ??= record => ServiceReadyFile.TryRead(record);
        string version = string.IsNullOrWhiteSpace(handshake.Version)
            ? CurrentVersion()
            : handshake.Version;
        ServiceLaunch launch = Launch(
            name,
            exePath,
            version,
            startProcess,
            processNameOrNull,
            handshake,
            started
        );
        if (!launch.Ready && !launch.Cancelled && launch.Record != null)
        {
            Rollback(null, new[] { launch.Record }, handshake);
        }

        return launch;
    }

    /// <summary>
    /// One role: check its prerequisites, give it a new nonce, start it, then wait for its ready
    /// file and first heartbeat. <paramref name="started"/> sees the record as soon as the
    /// process has a pid.
    /// </summary>
    private static ServiceLaunch Launch(
        string name,
        string exePath,
        string version,
        Func<string, string, int?> startProcess,
        Func<int, string> processNameOrNull,
        ServiceStartupHandshake handshake,
        Action<ServiceProcessRecord> started = null
    )
    {
        ServiceProcessRecord record = null;
        try
        {
            string arguments = ServiceProcessPlan.ArgumentsFor(name);
            string validation =
                arguments == null ? "unknown service role." : Validate(name, exePath, handshake);
            if (validation != null)
            {
                return new ServiceLaunch(
                    null,
                    false,
                    false,
                    Fail(handshake, $"{name} failed: {validation}")
                );
            }

            var pending = new ServiceProcessRecord
            {
                Name = name,
                Arguments = arguments,
                ExecutablePath = exePath,
                Nonce = Guid.NewGuid().ToString("N"),
                Version = version,
            };
            handshake.Pending = pending;
            int? pid = startProcess(name, arguments);
            if (pid is not int id || id <= 0)
            {
                return new ServiceLaunch(
                    null,
                    false,
                    false,
                    Fail(handshake, $"Failed to start {name} ({arguments}).")
                );
            }

            pending.Pid = id;
            record = pending;
            ServiceProcessProbe probed = handshake.Probe?.Invoke(id);
            if (!string.IsNullOrWhiteSpace(probed?.ExecutablePath))
            {
                record.ExecutablePath = probed.ExecutablePath;
            }

            record.StartedAt = probed?.StartedAt ?? DateTimeOffset.UtcNow;
            Console.WriteLine($"Started {name} pid {id} ({arguments}).");
            started?.Invoke(record);
            string failure = WaitForReady(record, processNameOrNull, handshake, out bool cancelled);
            return new ServiceLaunch(record, failure == null, cancelled, failure);
        }
        catch (Exception e)
        {
            return new ServiceLaunch(
                record,
                false,
                false,
                Fail(handshake, $"{name} failed: {e.Message}")
            );
        }
    }

    private static bool IsHeroesReplayExe(string exePath) =>
        !string.IsNullOrWhiteSpace(exePath)
        && ServiceProcessPlan.IsHeroesReplay(Path.GetFileName(exePath));

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
        Action<TimeSpan> pause = shutdown.Wait ?? Thread.Sleep;
        ServiceRoleStop supervisor = null;

        // Until the processes are probed, every recorded role counts as running.
        List<ServiceProcessRecord> survivors = recorded;
        try
        {
            // Probe before asking, so a role that exits at once is graceful, not already gone.
            List<ServiceProcessRecord> living = StillRunning(recorded, shutdown);
            var asked = new HashSet<ServiceProcessRecord>(living);
            shutdown.RequestGracefulStop?.Invoke();

            // The supervisor sees the same stop file and exits without restarting anything.
            supervisor = StopSupervisor(shutdown.StopSupervisor);
            if (supervisor != null)
            {
                Console.WriteLine("  " + supervisor.Describe());
                // A restart it began before it saw the stop file is in the lock by now.
                foreach (ServiceProcessRecord added in Added(recorded, lockPath))
                {
                    recorded.Add(added);
                    if (StillRunning(new[] { added }, shutdown).Count == 1)
                    {
                        living.Add(added);
                        asked.Add(added);
                    }
                }
            }

            RequestStreamShutdown(shutdown.ConfirmStream);
            bool hadSpectate = recorded.Any(record => record.Name == "spectate");

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
                Supervisor = supervisor,
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

            // A supervisor that is still running would restart roles once the stop file is gone.
            if (supervisor?.Outcome != ServiceStopOutcome.StillRunning)
            {
                shutdown.ClearStopFile?.Invoke();
            }
        }
    }

    private static List<ServiceProcessRecord> StillRunning(
        IEnumerable<ServiceProcessRecord> records,
        ServiceShutdown shutdown
    ) => ServiceProcessPlan.StillRunning(records, shutdown.ProcessNameOrNull, shutdown.Probe);

    private static ServiceRoleStop StopSupervisor(Func<ServiceRoleStop> stopSupervisor)
    {
        if (stopSupervisor == null)
        {
            return null;
        }

        try
        {
            return stopSupervisor();
        }
        catch (Exception e)
        {
            return new ServiceRoleStop(
                ServiceRoleLog.SupervisorRole,
                0,
                ServiceStopOutcome.StillRunning,
                e.Message
            );
        }
    }

    /// <summary>Records in the lock now that the stop's first read did not have.</summary>
    private static List<ServiceProcessRecord> Added(
        IReadOnlyCollection<ServiceProcessRecord> recorded,
        string lockPath
    )
    {
        var known = new HashSet<string>(
            recorded.Select(record => record.Nonce ?? "pid:" + record.Pid),
            StringComparer.Ordinal
        );
        return (ServiceLockStore.TryLoad(lockPath)?.Processes ?? new List<ServiceProcessRecord>())
            .Where(record =>
                record != null
                && record.Pid > 0
                && !known.Contains(record.Nonce ?? "pid:" + record.Pid)
            )
            .ToList();
    }

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

    /// <summary>
    /// Classify every role as ready, degraded, stale, stopped, or failed from the lock, the
    /// process table, and each role's heartbeat. Exit code 0 unless a role is failed, stale, or
    /// degraded.
    /// </summary>
    public static int Status(
        string lockPath,
        Func<int, string> processNameOrNull,
        SpectatorStatus spectator,
        Func<int, ServiceProcessProbe> probeOrNull = null,
        ServiceStatusQuery query = null
    )
    {
        query ??= new ServiceStatusQuery();
        TimeProvider time = query.Time ?? TimeProvider.System;
        DateTimeOffset now = time.GetUtcNow();
        ServiceStatusReport report = ServiceHealthClassifier.Build(
            ServiceLockStore.TryLoad(lockPath),
            processNameOrNull,
            probeOrNull,
            query.ReadHeartbeat ?? (record => ServiceReadyFile.TryRead(record)),
            query.StopRequested?.Invoke() == true,
            now,
            query.Settings ?? new ServiceHealthSettings(),
            query.Environment,
            spectator
        );
        string logs = query.LogDirectory ?? ServiceLogSettings.DefaultDirectory;
        DateOnly today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, time.LocalTimeZone).DateTime
        );
        report = ServiceHealthClassifier.WithLogPaths(
            report,
            role => ServiceRoleLog.LatestPath(logs, role, today)
        );
        report = ServiceHealthClassifier.WithSupervisor(
            report,
            query.ReadSupervisor?.Invoke(),
            query.SupervisorLiveness?.Invoke(),
            now
        );
        report = report with { Machine = ReadMachine(query.ReadMachine) };
        TextWriter output = query.Out ?? Console.Out;
        if (query.Output == CliOutputFormat.Json)
        {
            output.WriteLine(report.ToJson());
        }
        else
        {
            WriteStatusText(output, report, spectator);
        }

        return report.ExitCode;
    }

    public static void WriteStatusText(
        TextWriter output,
        ServiceStatusReport report,
        SpectatorStatus spectator
    )
    {
        output.WriteLine($"Services: {report.Message} [{report.Code}]");
        if (report.StopRequested)
        {
            output.WriteLine("  A stop request is pending (services.stop).");
        }

        WriteSupervisorText(output, report.Supervisor);
        foreach (ServiceRoleHealth role in report.Roles)
        {
            string state = role.State.ToString().ToLowerInvariant();
            if (!role.Expected)
            {
                output.WriteLine($"  {role.Role, -9}{state, -9}{role.Cause}");
                WriteLogText(output, role);
                continue;
            }

            var facts = new List<string> { $"pid {role.Pid}" };
            if (role.HeartbeatAgeSeconds is long beat)
            {
                facts.Add(
                    $"heartbeat {ServiceHealthClassifier.Describe(TimeSpan.FromSeconds(beat))} ago"
                );
            }

            if (role.WorkAgeSeconds is long work)
            {
                facts.Add(
                    $"last {ServiceHealthSettings.WorkName(role.Role)} {ServiceHealthClassifier.Describe(TimeSpan.FromSeconds(work))} ago"
                );
            }

            if (!string.IsNullOrWhiteSpace(role.Version))
            {
                facts.Add("version " + role.Version);
            }

            output.WriteLine($"  {role.Role, -9}{state, -9}{string.Join(", ", facts)}");
            output.WriteLine(
                string.IsNullOrWhiteSpace(role.CauseCode)
                    ? $"{"", 20}{role.Cause}"
                    : $"{"", 20}{role.Cause} [{role.CauseCode}]"
            );
            if (!string.IsNullOrWhiteSpace(role.Remediation))
            {
                output.WriteLine($"{"", 20}Fix: {role.Remediation}");
            }

            if (role.Restarts is ServiceRoleRestartStatus restarts)
            {
                string last = restarts.LastRestartAt is DateTimeOffset at
                    ? $", last {at.ToLocalTime():HH:mm:ss} ({restarts.LastReason})"
                    : string.Empty;
                string budget = restarts.BudgetExhausted
                    ? "budget exhausted"
                    : $"budget {restarts.BudgetUsed} of {restarts.BudgetLimit} used in {ServiceHealthClassifier.Describe(TimeSpan.FromSeconds(restarts.BudgetWindowSeconds))}";
                output.WriteLine($"{"", 20}Restarts: {restarts.Count}{last}; {budget}.");
            }

            WriteLogText(output, role);
        }

        WriteMachineText(output, report.Machine);
        if (spectator == null)
        {
            output.WriteLine("Spectator status: no snapshot.");
            return;
        }

        string obs = ObsStatus.Describe(spectator);
        output.WriteLine(
            $"Spectator status: phase={spectator.Phase} running={spectator.SpectatorRunning} stale={spectator.SnapshotStale} replay={spectator.ReplayId} map={spectator.Map} timer={spectator.Timer}"
                + (obs == null ? "" : " " + obs)
        );
        if (spectator.CompletedReplayId.HasValue)
        {
            output.WriteLine(
                $"Last completion: replay {spectator.CompletedReplayId} team {spectator.CompletedWinnerTeam} at {spectator.CompletedAt:O}"
            );
        }
    }

    private static void WriteSupervisorText(TextWriter output, ServiceSupervisorSummary supervisor)
    {
        if (supervisor == null)
        {
            output.WriteLine(
                "  Supervisor: not running. `heroesreplay services supervise` restarts failed and stale roles."
            );
            return;
        }

        if (!supervisor.Running)
        {
            string why = string.IsNullOrWhiteSpace(supervisor.Detail)
                ? string.Empty
                : "; " + supervisor.Detail;
            output.WriteLine(
                $"  Supervisor: not running (pid {supervisor.Pid} left supervisor.json{why}). Restart counts below are its last."
            );
            return;
        }

        string backoff = string.Join(
            "/",
            supervisor.BackoffSeconds.Select(seconds =>
                ServiceHealthClassifier.Describe(TimeSpan.FromSeconds(seconds))
            )
        );
        output.WriteLine(
            $"  Supervisor: running ({SeenVia(supervisor)}){(supervisor.Stopping ? ", stopping" : "")}, roles {string.Join(", ", supervisor.Supervised)}; backoff {backoff}; budget {supervisor.Budget} per {ServiceHealthClassifier.Describe(TimeSpan.FromSeconds(supervisor.BudgetWindowSeconds))}; stale roles killed after {ServiceHealthClassifier.Describe(TimeSpan.FromSeconds(supervisor.StaleRestartAfterSeconds))}."
        );
        if (!string.IsNullOrWhiteSpace(supervisor.LogPath))
        {
            output.WriteLine($"{"", 20}Log: {supervisor.LogPath}");
        }
    }

    /// <summary>
    /// "pid 14420, seen via supervisor.json; mutex not visible from this session" (#283), or
    /// "pid 14420, seen via its mutex".
    /// </summary>
    private static string SeenVia(ServiceSupervisorSummary supervisor)
    {
        string pid = supervisor.Pid is int value ? $"pid {value}, " : string.Empty;
        string via =
            supervisor.SeenVia == ServiceSupervisorLiveness.ViaStateFile
                ? "seen via supervisor.json"
                : "seen via its mutex";
        string detail = string.IsNullOrWhiteSpace(supervisor.Detail)
            ? string.Empty
            : "; " + supervisor.Detail;
        return pid + via + detail;
    }

    private static MachineHealthReport ReadMachine(Func<MachineHealthReport> read)
    {
        if (read == null)
        {
            return null;
        }

        try
        {
            return read();
        }
        catch (Exception e)
        {
            return new MachineHealthReport
            {
                Ok = false,
                Warnings = new[] { "The machine could not be read: " + e.Message },
            };
        }
    }

    private static void WriteMachineText(TextWriter output, MachineHealthReport machine)
    {
        if (machine == null)
        {
            return;
        }

        output.WriteLine($"Machine: {MachineHealth.Describe(machine)}.");
        foreach (string warning in machine.Warnings)
        {
            output.WriteLine($"  WARN {warning}");
        }
    }

    private static void WriteLogText(TextWriter output, ServiceRoleHealth role)
    {
        if (!string.IsNullOrWhiteSpace(role.LogPath))
        {
            output.WriteLine($"{"", 20}Log: {role.LogPath}");
        }
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

    /// <summary>Null once the role is ready and has beaten once; otherwise why not.</summary>
    private static string WaitForReady(
        ServiceProcessRecord record,
        Func<int, string> processNameOrNull,
        ServiceStartupHandshake handshake,
        out bool cancelled
    )
    {
        cancelled = false;
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
                    return Fail(handshake, $"{record.Name} failed: exited before heartbeat.");
                }

                if (ServiceChildHeartbeat.FollowsReady(report))
                {
                    record.ReadyAt = report.ReadyAt ?? DateTimeOffset.UtcNow;
                    record.HeartbeatAt = report.HeartbeatAt;
                    if (!string.IsNullOrWhiteSpace(report.Version))
                    {
                        record.Version = report.Version;
                    }

                    return null;
                }
            }
            else if (exited)
            {
                return Fail(handshake, $"{record.Name} failed: exited before ready.");
            }

            // A stop request ends the wait. The role sees the same stop file and exits.
            if (handshake.Cancelled?.Invoke() == true)
            {
                cancelled = true;
                return Fail(handshake, $"{record.Name}: a stop was requested before it was ready.");
            }

            if (handshake.ReadyTimeout <= TimeSpan.Zero || DateTimeOffset.UtcNow >= deadline)
            {
                return Fail(
                    handshake,
                    IsReady(record, report)
                        ? $"{record.Name} failed: heartbeat timed out."
                        : $"{record.Name} failed: ready timed out."
                );
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

    private static string Fail(ServiceStartupHandshake handshake, string message)
    {
        Console.Error.WriteLine(message);
        handshake?.Report?.Invoke(message);
        return message;
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
