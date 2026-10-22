using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.ServiceHost.Logs;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>What the service-state pruners must not touch: the processes that run, and the role nonces <c>services.json</c> tracks.</summary>
public sealed record ServiceStateFacts(
    IReadOnlySet<int> LivePids,
    IReadOnlySet<string> TrackedNonces
);

/// <summary>
/// The logs and ready files the roles leave behind (#494). A role pruned only its own log, and
/// only when its own writer opened a file, so a role that stopped, or never ran as that role,
/// kept its files forever; and a ready file went only on a clean stop, so every crash or kill
/// left one. On the stream PC: 84 files and 314 MB of logs, 65 stale ready files.
/// </summary>
public static class ServiceStatePruners
{
    public const string LogsPrunerName = "role-logs";
    public const string ReadyPrunerName = "ready-files";

    /// <summary>A pid file younger than this may still be read by the launch that wrote it.</summary>
    public static readonly TimeSpan PidFileGrace = TimeSpan.FromMinutes(10);

    private static readonly Regex RoleLogName = new(
        @"^(?<role>[A-Za-z0-9][A-Za-z0-9-]{0,31})-\d{4}-\d{2}-\d{2}(?:\.\d{1,5})?\.log$",
        RegexOptions.CultureInvariant
    );

    public static IPruner LogsPruner(AppSettings settings, string stateDirectory) =>
        new Pruner(
            LogsPrunerName,
            "Every role's log beyond ServiceLogs:RetainedDays or MaxFilesPerRole, whether or not the role runs; pid files of processes that are gone; services-start-console.log and legacy-*.zip after RetainedDays.",
            now =>
                PlanLogs(
                    LogsDirectory(settings, stateDirectory),
                    stateDirectory,
                    settings?.ServiceLogs ?? new ServiceLogSettings(),
                    LiveFacts(stateDirectory),
                    now
                )
        );

    public static IPruner ReadyPruner(AppSettings settings, string stateDirectory) =>
        new Pruner(
            ReadyPrunerName,
            "ready\\*.json of a process that is gone (after ServiceHealth:ReadyFileGoneGrace) or that has not heartbeat for ServiceHealth:ReadyFileMaxAge. A role services.json tracks is never touched.",
            now =>
                PlanReadyFiles(
                    Path.Combine(stateDirectory, "ready"),
                    settings?.ServiceHealth ?? new ServiceHealthSettings(),
                    LiveFacts(stateDirectory),
                    now
                )
        );

    public static string LogsDirectory(AppSettings settings, string stateDirectory) =>
        string.IsNullOrWhiteSpace(settings?.ServiceLogs?.Directory)
            ? Path.Combine(stateDirectory, "logs")
            : settings.ServiceLogs.ResolvedDirectory;

    /// <summary>The running processes and the nonces in <c>services.json</c> under <paramref name="stateDirectory"/>.</summary>
    public static ServiceStateFacts LiveFacts(string stateDirectory)
    {
        var nonces = new HashSet<string>(StringComparer.Ordinal);
        ServiceLock snapshot = ServiceLockStore.TryLoad(
            Path.Combine(stateDirectory, Path.GetFileName(ServiceLockStore.DefaultPath))
        );
        foreach (
            ServiceProcessRecord record in snapshot?.Processes ?? new List<ServiceProcessRecord>()
        )
        {
            if (!string.IsNullOrWhiteSpace(record?.Nonce))
            {
                nonces.Add(record.Nonce);
            }
        }

        return new ServiceStateFacts(
            ProcessTable.Snapshot().Select(process => process.Pid).ToHashSet(),
            nonces
        );
    }

    /// <summary>What the logs pruner removes now. Writes nothing.</summary>
    public static PrunePlan PlanLogs(
        string logs,
        string stateDirectory,
        ServiceLogSettings settings,
        ServiceStateFacts facts,
        DateTimeOffset utcNow
    )
    {
        var plan = new PrunePlan(LogsPrunerName);
        settings ??= new ServiceLogSettings();
        DateTime before = utcNow.AddDays(-settings.Days).UtcDateTime;
        if (!string.IsNullOrWhiteSpace(logs) && Directory.Exists(logs))
        {
            var folder = new DirectoryInfo(logs);
            DateOnly today = DateOnly.FromDateTime(utcNow.LocalDateTime);
            IEnumerable<string> roles = folder
                .GetFiles("*.log")
                .Select(file => RoleLogName.Match(file.Name))
                .Where(match => match.Success)
                .Select(match => match.Groups["role"].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (string role in roles)
            {
                PlanRoleLogs(logs, role, today, settings, plan);
            }

            foreach (FileInfo console in folder.GetFiles("services-start-console*.log"))
            {
                if (console.LastWriteTimeUtc < before)
                {
                    plan.Add(
                        PruneItem.ForFile(
                            console,
                            $"services start console log not written for ServiceLogs:RetainedDays ({settings.Days})"
                        )
                    );
                }
            }

            foreach (FileInfo archive in folder.GetFiles("legacy-*.zip"))
            {
                if (archive.LastWriteTimeUtc < before)
                {
                    plan.Add(
                        PruneItem.ForFile(
                            archive,
                            $"archived logs older than ServiceLogs:RetainedDays ({settings.Days})"
                        )
                    );
                }
            }

            PlanPidFiles(folder, facts, utcNow, plan);
        }

        if (!string.IsNullOrWhiteSpace(stateDirectory) && Directory.Exists(stateDirectory))
        {
            PlanPidFiles(new DirectoryInfo(stateDirectory), facts, utcNow, plan);
        }

        return plan;
    }

    /// <summary>
    /// The rule each role's writer applies (<see cref="ServiceRoleLog.Prune"/>): files from more
    /// than RetainedDays ago, then all but the newest MaxFilesPerRole. A file the role still writes is today's, so it stays; one held open is skipped when it is deleted.
    /// </summary>
    private static void PlanRoleLogs(
        string logs,
        string role,
        DateOnly today,
        ServiceLogSettings settings,
        PrunePlan plan
    )
    {
        DateOnly oldest = today.AddDays(-(settings.Days - 1));
        int kept = 0;
        foreach (ServiceRoleLogFile file in ServiceRoleLog.Files(logs, role))
        {
            if (file.Day >= oldest && kept < settings.MaxFiles)
            {
                kept++;
                continue;
            }

            var info = new FileInfo(file.Path);
            if (!info.Exists)
            {
                continue;
            }

            plan.Add(
                PruneItem.ForFile(
                    info,
                    file.Day < oldest
                        ? $"{role} log from {file.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, older than ServiceLogs:RetainedDays ({settings.Days})"
                        : $"{role} log beyond ServiceLogs:MaxFilesPerRole ({settings.MaxFiles})"
                )
            );
        }
    }

    private static void PlanPidFiles(
        DirectoryInfo folder,
        ServiceStateFacts facts,
        DateTimeOffset utcNow,
        PrunePlan plan
    )
    {
        foreach (FileInfo pidFile in folder.GetFiles("*.pid"))
        {
            if (pidFile.LastWriteTimeUtc >= (utcNow - PidFileGrace).UtcDateTime)
            {
                continue;
            }

            int? pid = ReadPid(pidFile.FullName);
            if (pid is int alive && facts?.LivePids?.Contains(alive) == true)
            {
                continue;
            }

            plan.Add(
                PruneItem.ForFile(
                    pidFile,
                    pid is int gone
                        ? $"pid file of process {gone}, which is gone"
                        : "pid file with no readable pid"
                )
            );
        }
    }

    /// <summary>What the ready-file pruner removes now. Writes nothing.</summary>
    public static PrunePlan PlanReadyFiles(
        string ready,
        ServiceHealthSettings health,
        ServiceStateFacts facts,
        DateTimeOffset utcNow
    )
    {
        var plan = new PrunePlan(ReadyPrunerName);
        if (string.IsNullOrWhiteSpace(ready) || !Directory.Exists(ready))
        {
            return plan;
        }

        health ??= new ServiceHealthSettings();
        foreach (FileInfo file in new DirectoryInfo(ready).GetFiles("*.json"))
        {
            string nonce = Path.GetFileNameWithoutExtension(file.Name);
            if (facts?.TrackedNonces?.Contains(nonce) == true)
            {
                continue;
            }

            ServiceReadyReport report = ServiceReadyFile.IsSafeNonce(nonce)
                ? ServiceReadyFile.TryRead(new ServiceProcessRecord { Nonce = nonce }, ready)
                : null;
            DateTimeOffset beat = report?.HeartbeatAt ?? report?.ReadyAt ?? file.LastWriteTimeUtc;
            TimeSpan quiet = utcNow - beat;
            bool running = report?.Pid is int pid && facts?.LivePids?.Contains(pid) == true;
            if (!running && quiet >= health.EffectiveReadyFileGoneGrace)
            {
                plan.Add(
                    PruneItem.ForFile(
                        file,
                        report?.Pid is int gone
                            ? $"{report.Role ?? "role"} pid {gone} is gone, last heartbeat {Describe(quiet)} ago"
                            : $"no readable role heartbeat, written {Describe(quiet)} ago"
                    )
                );
            }
            else if (running && quiet >= health.EffectiveReadyFileMaxAge)
            {
                plan.Add(
                    PruneItem.ForFile(
                        file,
                        $"last heartbeat {Describe(quiet)} ago, beyond ServiceHealth:ReadyFileMaxAge: pid {report.Pid} is another process now"
                    )
                );
            }
        }

        return plan;
    }

    private static int? ReadPid(string path)
    {
        try
        {
            string text = File.ReadAllText(path).Trim();
            return
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid)
                && pid > 0
                ? pid
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Describe(TimeSpan span) =>
        span.TotalDays >= 1 ? span.TotalDays.ToString("0.#", CultureInfo.InvariantCulture) + " days"
        : span.TotalHours >= 1
            ? span.TotalHours.ToString("0.#", CultureInfo.InvariantCulture) + " h"
        : Math.Max(0, span.TotalMinutes).ToString("0", CultureInfo.InvariantCulture) + " min";
}
