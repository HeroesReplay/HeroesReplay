using System;
using System.Collections.Generic;
using System.Globalization;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>One watched process's private bytes.</summary>
public sealed record MachineProcessMemory
{
    public string Name { get; init; }
    public int Pid { get; init; }
    public long PrivateMegabytes { get; init; }
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
    public IReadOnlyList<MachineProcessMemory> Processes { get; init; } =
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
        if (snapshot.PhysicalTotalBytes > 0 && physicalPercent > settings.MemoryWarnPercent)
        {
            warnings.Add(
                $"Physical memory is {Format(physicalPercent)}% in use ({Gigabytes(physicalUsed)} of {Gigabytes(snapshot.PhysicalTotalBytes)} GB), above {Format(settings.MemoryWarnPercent)}%."
            );
        }

        if (snapshot.CommitLimitBytes > 0 && commitPercent > settings.CommitWarnPercent)
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
        };
    }

    /// <summary>One line for <c>services status</c> and the supervisor log.</summary>
    public static string Describe(MachineHealthReport report) =>
        report == null
            ? "unreadable"
            : $"memory {Format(report.PhysicalUsedPercent)}% ({report.PhysicalUsedMegabytes} of {report.PhysicalTotalMegabytes} MB), commit {Format(report.CommitPercent)}% ({report.CommitMegabytes} of {report.CommitLimitMegabytes} MB), Agent.exe {report.AgentProcesses}, conhost.exe {report.ConhostProcesses}, HeroesOfTheStorm_x64.exe {report.HeroesProcesses}";

    private static double Percent(long part, long whole) => whole > 0 ? 100.0 * part / whole : 0;

    private static string Format(double value) =>
        Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);

    private static string Gigabytes(long bytes) =>
        (bytes / (1024.0 * Megabyte)).ToString("0.0", CultureInfo.InvariantCulture);
}
