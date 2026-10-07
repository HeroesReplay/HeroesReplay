using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// Reads the machine for <see cref="MachineHealth"/>: memory and commit charge from
/// <c>GetPerformanceInfo</c>, process counts from <see cref="ProcessTable"/>, and the watched
/// processes' private bytes. Read-only.
/// </summary>
public static class MachineHealthProbe
{
    private const string AgentImage = "Agent.exe";
    private const string ConhostImage = "conhost.exe";

    public static MachineHealthSnapshot Read(MachineHealthSettings settings)
    {
        settings ??= new MachineHealthSettings();
        var performance = new PerformanceInformation
        {
            cb = (uint)Marshal.SizeOf<PerformanceInformation>(),
        };
        bool memory = GetPerformanceInfo(ref performance, performance.cb);
        long page = memory ? (long)performance.PageSize : 0;
        int agents = 0;
        int conhosts = 0;
        int heroes = 0;
        foreach (ProcessTableEntry process in ProcessTable.Snapshot())
        {
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

        return new MachineHealthSnapshot
        {
            PhysicalTotalBytes = (long)performance.PhysicalTotal * page,
            PhysicalAvailableBytes = (long)performance.PhysicalAvailable * page,
            CommitBytes = (long)performance.CommitTotal * page,
            CommitLimitBytes = (long)performance.CommitLimit * page,
            AgentProcesses = agents,
            ConhostProcesses = conhosts,
            HeroesProcesses = heroes,
            Processes = Watched(settings.Watched),
        };
    }

    private static List<MachineProcessMemory> Watched(IReadOnlyList<string> names)
    {
        var watched = new List<MachineProcessMemory>();
        foreach (string name in names)
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                try
                {
                    watched.Add(
                        new MachineProcessMemory
                        {
                            Name = process.ProcessName,
                            Pid = process.Id,
                            PrivateMegabytes = process.PrivateMemorySize64 / (1024 * 1024),
                            StartedAt = StartedAt(process),
                        }
                    );
                }
                catch (InvalidOperationException) { }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return watched;
    }

    private static DateTimeOffset? StartedAt(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime);
        }
        catch (Exception e)
            when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
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
