using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HeroesReplay.CLI;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Processes;

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
        Console.WriteLine(
            "Twitch ingest was not started. Streaming stays at OBS:StreamingEnabled."
        );
        Console.WriteLine(
            $"OpenTelemetry export: {AspireDashboardHost.OtlpGrpcEndpoint} (Aspire dashboard {AspireDashboardHost.UiUrl})."
        );
        return 0;
    }

    public static int Stop(
        string lockPath,
        Func<int, string> processNameOrNull,
        Action<int> kill,
        Action requestGracefulStop = null,
        TimeSpan? gracefulWait = null,
        Action<TimeSpan> wait = null,
        Action clearStopFile = null,
        Action stopSpectatedGame = null,
        Func<int, ServiceProcessProbe> probeOrNull = null
    )
    {
        requestGracefulStop?.Invoke();
        ServiceLock snapshot = ServiceLockStore.TryLoad(lockPath);
        bool hadSpectate = snapshot?.Processes?.Any(record => record?.Name == "spectate") == true;
        TimeSpan budget = gracefulWait ?? TimeSpan.FromSeconds(20);
        Action<TimeSpan> pause = wait ?? Thread.Sleep;
        try
        {
            List<ServiceProcessRecord> living = ServiceProcessPlan.StillRunning(
                snapshot?.Processes,
                processNameOrNull,
                probeOrNull
            );
            bool sawAny = living.Count > 0;
            DateTimeOffset until = DateTimeOffset.UtcNow + budget;
            while (living.Count > 0 && DateTimeOffset.UtcNow < until)
            {
                pause(TimeSpan.FromMilliseconds(200));
                living = ServiceProcessPlan.StillRunning(
                    ServiceLockStore.TryLoad(lockPath)?.Processes,
                    processNameOrNull,
                    probeOrNull
                );
            }

            if (living.Count == 0)
            {
                if (hadSpectate)
                {
                    stopSpectatedGame?.Invoke();
                }

                Console.WriteLine(
                    sawAny ? "Services stopped." : "No HeroesReplay services are running."
                );
                return 0;
            }

            foreach (ServiceProcessRecord record in living)
            {
                try
                {
                    kill(record.Pid);
                    Console.WriteLine($"Stopped {record.Name} pid {record.Pid}.");
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(
                        $"Could not stop {record.Name} pid {record.Pid}: {e.Message}"
                    );
                }
            }

            if (hadSpectate)
            {
                stopSpectatedGame?.Invoke();
            }

            Console.WriteLine(
                "Forced stop skipped spectator shutdown. Heroes of the Storm was closed if it was still open."
            );
            return 0;
        }
        finally
        {
            if (snapshot?.Processes != null)
            {
                foreach (ServiceProcessRecord record in snapshot.Processes)
                {
                    ServiceReadyFile.Delete(record?.Nonce);
                }
            }

            ServiceLockStore.Delete(lockPath);
            clearStopFile?.Invoke();
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
            Console.WriteLine(
                $"Spectator status: phase={spectator.Phase} running={spectator.SpectatorRunning} stale={spectator.SnapshotStale} replay={spectator.ReplayId} map={spectator.Map} timer={spectator.Timer}"
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
            // A ready report wins over a process-name miss so tests can report ready without a live PID.
            if (IsReady(record, report))
            {
                record.ReadyAt = report.ReadyAt ?? DateTimeOffset.UtcNow;
                record.HeartbeatAt = report.HeartbeatAt ?? record.ReadyAt;
                if (!string.IsNullOrWhiteSpace(report.Version))
                {
                    record.Version = report.Version;
                }

                return true;
            }

            string processName = processNameOrNull(record.Pid);
            if (!ServiceProcessPlan.IsHeroesReplay(processName))
            {
                Fail(handshake, $"{record.Name} failed: exited before ready.");
                return false;
            }

            if (handshake.ReadyTimeout <= TimeSpan.Zero || DateTimeOffset.UtcNow >= deadline)
            {
                Fail(handshake, $"{record.Name} failed: ready timed out.");
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
