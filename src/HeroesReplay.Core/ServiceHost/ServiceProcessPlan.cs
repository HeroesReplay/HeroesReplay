using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace HeroesReplay.Core.ServiceHost;

public sealed class ServiceProcessRecord
{
    public string Name { get; set; }
    public int Pid { get; set; }
    public string Arguments { get; set; }
    public string ExecutablePath { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public string Nonce { get; set; }
    public string Version { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
    public DateTimeOffset? HeartbeatAt { get; set; }
}

public sealed class ServiceProcessProbe
{
    public string ExecutablePath { get; set; }
    public DateTimeOffset? StartedAt { get; set; }

    public static ServiceProcessProbe TryFromProcess(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            string path = null;
            try
            {
                path = process.MainModule?.FileName;
            }
            catch (Exception)
            {
                path = null;
            }

            DateTimeOffset? started = null;
            try
            {
                started = new DateTimeOffset(process.StartTime).ToUniversalTime();
            }
            catch (Exception)
            {
                started = null;
            }

            return new ServiceProcessProbe { ExecutablePath = path, StartedAt = started };
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public sealed class ServiceLock
{
    public DateTimeOffset StartedAt { get; set; }
    public List<ServiceProcessRecord> Processes { get; set; } = new();
}

public static class ServiceProcessPlan
{
    public const string ProcessName = "heroesreplay";
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);

    public static IReadOnlyList<(string Name, string Arguments)> All { get; } =
        new (string, string)[]
        {
            ("spectate", "spectate heroesprofile"),
            ("twitch", "twitch connect"),
            ("download", "heroesprofile download"),
            ("youtube", "youtube uploader"),
        };

    public static bool IsHeroesReplay(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        string name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
        return string.Equals(name, ProcessName, StringComparison.OrdinalIgnoreCase);
    }

    public static List<ServiceProcessRecord> StillRunning(
        IEnumerable<ServiceProcessRecord> records,
        Func<int, string> processNameOrNull,
        Func<int, ServiceProcessProbe> probeOrNull = null
    )
    {
        var living = new List<ServiceProcessRecord>();
        if (records == null || processNameOrNull == null)
        {
            return living;
        }

        foreach (ServiceProcessRecord record in records)
        {
            if (record == null || record.Pid <= 0)
            {
                continue;
            }

            if (!IsHeroesReplay(processNameOrNull(record.Pid)))
            {
                continue;
            }

            // A recycled PID can still be named heroesreplay. Path and start time must agree when both sides know them.
            if (HasIdentity(record))
            {
                ServiceProcessProbe probe =
                    probeOrNull != null
                        ? probeOrNull(record.Pid)
                        : ServiceProcessProbe.TryFromProcess(record.Pid);
                if (!IdentityMatches(record, probe))
                {
                    continue;
                }
            }

            living.Add(record);
        }

        return living;
    }

    private static bool HasIdentity(ServiceProcessRecord record)
    {
        return !string.IsNullOrWhiteSpace(record.ExecutablePath) || record.StartedAt.HasValue;
    }

    private static bool IdentityMatches(ServiceProcessRecord record, ServiceProcessProbe probe)
    {
        if (probe == null)
        {
            return true;
        }

        if (
            !string.IsNullOrWhiteSpace(record.ExecutablePath)
            && !string.IsNullOrWhiteSpace(probe.ExecutablePath)
            && !SamePath(record.ExecutablePath, probe.ExecutablePath)
        )
        {
            return false;
        }

        if (
            record.StartedAt.HasValue
            && probe.StartedAt.HasValue
            && (record.StartedAt.Value - probe.StartedAt.Value).Duration() > StartTimeTolerance
        )
        {
            return false;
        }

        return true;
    }

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
