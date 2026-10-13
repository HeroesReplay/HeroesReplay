using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>One process's private bytes (its commit charge) and working set.</summary>
public sealed record MachineProcessMemory
{
    public string Name { get; init; }
    public int Pid { get; init; }
    public long PrivateMegabytes { get; init; }
    public long WorkingSetMegabytes { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
}

/// <summary>What <see cref="MachineHealthProbe"/> read. Byte counts are zero when unreadable.</summary>
public sealed record MachineHealthSnapshot
{
    public long PhysicalTotalBytes { get; init; }
    public long PhysicalAvailableBytes { get; init; }
    public long CommitBytes { get; init; }
    public long CommitLimitBytes { get; init; }
    public int AgentProcesses { get; init; }
    public int ConhostProcesses { get; init; }
    public int HeroesProcesses { get; init; }

    /// <summary>The <see cref="MachineHealthSettings.Watched"/> processes.</summary>
    public IReadOnlyList<MachineProcessMemory> Processes { get; init; } =
        Array.Empty<MachineProcessMemory>();

    /// <summary>
    /// Every process the probe read, whoever owns it. <see cref="MachineHealth.Evaluate"/> picks
    /// the top consumers from it.
    /// </summary>
    public IReadOnlyList<MachineProcessMemory> AllProcesses { get; init; } =
        Array.Empty<MachineProcessMemory>();
}

/// <summary>
/// The <c>machine</c> section of <c>services status</c>: memory, commit charge, and the process
/// counts that leak (#251), with a warning for each value above its limit.
/// </summary>
public sealed record MachineHealthReport
{
    /// <summary>False when any value is above its limit.</summary>
    public bool Ok { get; init; }
    public long PhysicalTotalMegabytes { get; init; }
    public long PhysicalUsedMegabytes { get; init; }
    public double PhysicalUsedPercent { get; init; }
    public long CommitMegabytes { get; init; }
    public long CommitLimitMegabytes { get; init; }
    public double CommitPercent { get; init; }
    public int AgentProcesses { get; init; }
    public int ConhostProcesses { get; init; }
    public int HeroesProcesses { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<MachineProcessMemory> Processes { get; init; } =
        Array.Empty<MachineProcessMemory>();

    /// <summary>
    /// Only while physical memory or commit is above its limit: the processes that hold the most
    /// private bytes (commit), whoever owns them, largest first (#399). Empty otherwise. Report
    /// only: nothing reads it to stop or change a process.
    /// </summary>
    public IReadOnlyList<MachineProcessMemory> TopConsumers { get; init; } =
        Array.Empty<MachineProcessMemory>();
}

public static class MachineHealth
{
    private const long Megabyte = 1024 * 1024;

    public static MachineHealthReport Evaluate(
        MachineHealthSnapshot snapshot,
        MachineHealthSettings settings
    )
    {
        snapshot ??= new MachineHealthSnapshot();
        settings ??= new MachineHealthSettings();
        long physicalUsed = Math.Max(
            0,
            snapshot.PhysicalTotalBytes - snapshot.PhysicalAvailableBytes
        );
        double physicalPercent = Percent(physicalUsed, snapshot.PhysicalTotalBytes);
        double commitPercent = Percent(snapshot.CommitBytes, snapshot.CommitLimitBytes);
        var warnings = new List<string>();
        bool memoryHigh =
            snapshot.PhysicalTotalBytes > 0 && physicalPercent > settings.MemoryWarnPercent;
        bool commitHigh =
            snapshot.CommitLimitBytes > 0 && commitPercent > settings.CommitWarnPercent;
        if (memoryHigh)
        {
            warnings.Add(
                $"Physical memory is {Format(physicalPercent)}% in use ({Gigabytes(physicalUsed)} of {Gigabytes(snapshot.PhysicalTotalBytes)} GB), above {Format(settings.MemoryWarnPercent)}%."
            );
        }

        if (commitHigh)
        {
            warnings.Add(
                $"Commit charge is {Format(commitPercent)}% of the limit ({Gigabytes(snapshot.CommitBytes)} of {Gigabytes(snapshot.CommitLimitBytes)} GB), above {Format(settings.CommitWarnPercent)}%."
            );
        }

        if (snapshot.AgentProcesses > settings.AgentWarnCount)
        {
            warnings.Add(
                $"{snapshot.AgentProcesses} Battle.net Agent.exe processes are running, above {settings.AgentWarnCount}. Spectate reaps leftovers before each Battle.net launch (BattleNetAgents:Enabled)."
            );
        }

        if (snapshot.ConhostProcesses > settings.ConhostWarnCount)
        {
            warnings.Add(
                $"{snapshot.ConhostProcesses} conhost.exe processes are running, above {settings.ConhostWarnCount}."
            );
        }

        if (snapshot.HeroesProcesses > settings.HeroesWarnCount)
        {
            warnings.Add(
                $"{snapshot.HeroesProcesses} HeroesOfTheStorm_x64.exe processes are running, above {settings.HeroesWarnCount}."
            );
        }

        return new MachineHealthReport
        {
            Ok = warnings.Count == 0,
            PhysicalTotalMegabytes = snapshot.PhysicalTotalBytes / Megabyte,
            PhysicalUsedMegabytes = physicalUsed / Megabyte,
            PhysicalUsedPercent = Math.Round(physicalPercent, 1),
            CommitMegabytes = snapshot.CommitBytes / Megabyte,
            CommitLimitMegabytes = snapshot.CommitLimitBytes / Megabyte,
            CommitPercent = Math.Round(commitPercent, 1),
            AgentProcesses = snapshot.AgentProcesses,
            ConhostProcesses = snapshot.ConhostProcesses,
            HeroesProcesses = snapshot.HeroesProcesses,
            Warnings = warnings,
            Processes = snapshot.Processes ?? Array.Empty<MachineProcessMemory>(),
            TopConsumers =
                memoryHigh || commitHigh
                    ? TopConsumers(snapshot.AllProcesses, settings.TopConsumerCount)
                    : Array.Empty<MachineProcessMemory>(),
        };
    }

    /// <summary>
    /// The <paramref name="count"/> processes with the most private bytes, largest first. A tie
    /// goes to the larger working set, then the lower pid. A process without a pid (the idle
    /// process) is not one.
    /// </summary>
    public static IReadOnlyList<MachineProcessMemory> TopConsumers(
        IEnumerable<MachineProcessMemory> processes,
        int count
    )
    {
        if (processes == null || count <= 0)
        {
            return Array.Empty<MachineProcessMemory>();
        }

        return processes
            .Where(process => process != null && process.Pid > 0)
            .OrderByDescending(process => process.PrivateMegabytes)
            .ThenByDescending(process => process.WorkingSetMegabytes)
            .ThenBy(process => process.Pid)
            .Take(count)
            .ToList();
    }

    /// <summary>One line for <c>services status</c> and the supervisor log.</summary>
    public static string Describe(MachineHealthReport report) =>
        report == null
            ? "unreadable"
            : $"memory {Format(report.PhysicalUsedPercent)}% ({report.PhysicalUsedMegabytes} of {report.PhysicalTotalMegabytes} MB), commit {Format(report.CommitPercent)}% ({report.CommitMegabytes} of {report.CommitLimitMegabytes} MB), Agent.exe {report.AgentProcesses}, conhost.exe {report.ConhostProcesses}, HeroesOfTheStorm_x64.exe {report.HeroesProcesses}";

    /// <summary>
    /// "msedge pid 18080: 13188 MB private, 10 MB working set, started 2026-10-08 11:52" (local
    /// time).
    /// </summary>
    public static string DescribeProcess(MachineProcessMemory process)
    {
        if (process == null)
        {
            return string.Empty;
        }

        string started = process.StartedAt is DateTimeOffset at
            ? $", started {at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}"
            : string.Empty;
        return $"{process.Name} pid {process.Pid}: {process.PrivateMegabytes} MB private, {process.WorkingSetMegabytes} MB working set{started}";
    }

    private static double Percent(long part, long whole) => whole > 0 ? 100.0 * part / whole : 0;

    private static string Format(double value) =>
        Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);

    private static string Gigabytes(long bytes) =>
        (bytes / (1024.0 * Megabyte)).ToString("0.0", CultureInfo.InvariantCulture);
}
