using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// Reads the machine for <see cref="MachineHealth"/>: memory and commit charge from
/// <c>GetPerformanceInfo</c>, process counts and start times from <see cref="ProcessTable"/>, and
/// every process's private bytes and working set from the system process list. Read-only: it
/// opens no process for anything but its start time, and never stops or changes one.
/// </summary>
public static class MachineHealthProbe
{
    private const string AgentImage = "Agent.exe";
    private const string ConhostImage = "conhost.exe";
    private const long Megabyte = 1024 * 1024;

    public static MachineHealthSnapshot Read(MachineHealthSettings settings)
    {
        var performance = new PerformanceInformation
        {
            cb = (uint)Marshal.SizeOf<PerformanceInformation>(),
        };
        bool memory = GetPerformanceInfo(ref performance, performance.cb);
        long page = memory ? (long)performance.PageSize : 0;
        MachineHealthSnapshot counters = new()
        {
            PhysicalTotalBytes = (long)performance.PhysicalTotal * page,
            PhysicalAvailableBytes = (long)performance.PhysicalAvailable * page,
            CommitBytes = (long)performance.CommitTotal * page,
            CommitLimitBytes = (long)performance.CommitLimit * page,
        };
        return Compose(counters, ProcessTable.Snapshot(), ReadProcessMemory(), settings);
    }

    /// <summary>
    /// The snapshot from the memory counters, the process table, and each process's memory as the
    /// system process list read it. A memory entry takes its start time from the table entry with
    /// the same pid and name; a pid that changed hands between the two reads has none.
    /// </summary>
    internal static MachineHealthSnapshot Compose(
        MachineHealthSnapshot counters,
        IReadOnlyList<ProcessTableEntry> table,
        IReadOnlyList<MachineProcessMemory> memory,
        MachineHealthSettings settings
    )
    {
        counters ??= new MachineHealthSnapshot();
        settings ??= new MachineHealthSettings();
        int agents = 0;
        int conhosts = 0;
        int heroes = 0;
        var byPid = new Dictionary<int, ProcessTableEntry>();
        foreach (ProcessTableEntry process in table ?? Array.Empty<ProcessTableEntry>())
        {
            if (process == null)
            {
                continue;
            }

            byPid[process.Pid] = process;
            string name = process.Name ?? string.Empty;
            if (string.Equals(name, AgentImage, StringComparison.OrdinalIgnoreCase))
            {
                agents++;
            }
            else if (string.Equals(name, ConhostImage, StringComparison.OrdinalIgnoreCase))
            {
                conhosts++;
            }
            else if (
                string.Equals(
                    Path.GetFileNameWithoutExtension(name),
                    NamedProcess.HeroesOfTheStorm,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                heroes++;
            }
        }

        var all = new List<MachineProcessMemory>();
        foreach (MachineProcessMemory process in memory ?? Array.Empty<MachineProcessMemory>())
        {
            if (process == null)
            {
                continue;
            }

            DateTimeOffset? started =
                byPid.TryGetValue(process.Pid, out ProcessTableEntry entry)
                && string.Equals(
                    Path.GetFileNameWithoutExtension(entry.Name ?? string.Empty),
                    process.Name,
                    StringComparison.OrdinalIgnoreCase
                )
                    ? entry.StartTime
                    : null;
            all.Add(process with { StartedAt = started ?? process.StartedAt });
        }

        return counters with
        {
            AgentProcesses = agents,
            ConhostProcesses = conhosts,
            HeroesProcesses = heroes,
            Processes = Watched(all, settings.Watched),
            AllProcesses = all,
        };
    }

    private static List<MachineProcessMemory> Watched(
        IReadOnlyList<MachineProcessMemory> all,
        IReadOnlyList<string> names
    )
    {
        var watched = new List<MachineProcessMemory>();
        foreach (string name in names)
        {
            watched.AddRange(
                all.Where(process =>
                        string.Equals(process.Name, name, StringComparison.OrdinalIgnoreCase)
                    )
                    .OrderBy(process => process.Pid)
            );
        }

        return watched;
    }

    // Process.GetProcesses reads the system process list once: the name, pid, private bytes, and
    // working set of every process come from it, without opening any of them.
    private static List<MachineProcessMemory> ReadProcessMemory()
    {
        var memory = new List<MachineProcessMemory>();
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            return memory;
        }

        foreach (Process process in processes)
        {
            try
            {
                memory.Add(
                    new MachineProcessMemory
                    {
                        Name = process.ProcessName,
                        Pid = process.Id,
                        PrivateMegabytes = process.PrivateMemorySize64 / Megabyte,
                        WorkingSetMegabytes = process.WorkingSet64 / Megabyte,
                    }
                );
            }
            catch (InvalidOperationException) { }
            finally
            {
                process.Dispose();
            }
        }

        return memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint cb;
        public UIntPtr CommitTotal;
        public UIntPtr CommitLimit;
        public UIntPtr CommitPeak;
        public UIntPtr PhysicalTotal;
        public UIntPtr PhysicalAvailable;
        public UIntPtr SystemCache;
        public UIntPtr KernelTotal;
        public UIntPtr KernelPaged;
        public UIntPtr KernelNonpaged;
        public UIntPtr PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetPerformanceInfo(
        ref PerformanceInformation information,
        uint size
    );
}
