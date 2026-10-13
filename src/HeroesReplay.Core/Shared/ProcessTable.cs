using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace HeroesReplay.Core.Shared;

/// <summary>
/// One process in a <see cref="ProcessTable"/> snapshot. <see cref="Name"/> is the toolhelp
/// exe name and is always set. <see cref="ImagePath"/> and <see cref="StartTime"/> are null when
/// this process could not open the other one (an elevated or protected process).
/// </summary>
public sealed record ProcessTableEntry(
    int Pid,
    int ParentPid,
    string Name,
    string ImagePath,
    DateTimeOffset? StartTime
);

public enum ProcessKillResult
{
    Killed,

    /// <summary>The process had already exited.</summary>
    Gone,

    /// <summary>The pid now belongs to another process (its start time changed). Not killed.</summary>
    Replaced,
    AccessDenied,
    Failed,
}

/// <summary>
/// The process table as a port (#409): what a detached OBS launch looks for after its start and
/// what a role's tree kill walks. Tests give a fake table; <see cref="WindowsProcessTable"/> is
/// the real one.
/// </summary>
public interface IProcessTable
{
    IReadOnlyList<ProcessTableEntry> Snapshot();

    /// <summary>The running process with this pid, or null.</summary>
    ProcessTableEntry Find(int pid);

    /// <summary>Kills the entry only while its pid still has the start time the table saw.</summary>
    ProcessKillResult Kill(ProcessTableEntry entry);
}

/// <summary>The real <see cref="IProcessTable"/>: <see cref="ProcessTable"/>.</summary>
public sealed class WindowsProcessTable : IProcessTable
{
    public static readonly WindowsProcessTable Instance = new();

    private WindowsProcessTable() { }

    public IReadOnlyList<ProcessTableEntry> Snapshot() => ProcessTable.Snapshot();

    public ProcessTableEntry Find(int pid) => ProcessTable.Find(pid);

    public ProcessKillResult Kill(ProcessTableEntry entry) => ProcessTable.Kill(entry);
}

/// <summary>
/// The Windows process table with parent pids (toolhelp snapshot), image paths, and start times.
/// <see cref="System.Diagnostics.Process"/> has no parent pid, and its <c>MainModule</c> throws
/// for processes this one cannot read.
/// </summary>
public static class ProcessTable
{
    private const uint SnapProcess = 0x00000002;
    private const uint ProcessTerminate = 0x0001;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidParameter = 87;
    private static readonly IntPtr InvalidHandle = new(-1);

    public static IReadOnlyList<ProcessTableEntry> Snapshot()
    {
        var entries = new List<ProcessTableEntry>();
        IntPtr snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == InvalidHandle || snapshot == IntPtr.Zero)
        {
            return entries;
        }

        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry))
            {
                return entries;
            }

            do
            {
                int pid = (int)entry.th32ProcessID;
                ReadDetails(pid, out string imagePath, out DateTimeOffset? startTime);
                entries.Add(
                    new ProcessTableEntry(
                        pid,
                        (int)entry.th32ParentProcessID,
                        entry.szExeFile,
                        imagePath,
                        startTime
                    )
                );
            } while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return entries;
    }

    /// <summary>
    /// The running process with <paramref name="pid"/>, or null when there is none. Only that
    /// process is opened for its image path and start time, so this is cheaper than
    /// <see cref="Snapshot"/> for one pid. The start time tells a reused pid apart.
    /// </summary>
    public static ProcessTableEntry Find(int pid)
    {
        if (pid <= 0)
        {
            return null;
        }

        IntPtr snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == InvalidHandle || snapshot == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry))
            {
                return null;
            }

            do
            {
                if ((int)entry.th32ProcessID != pid)
                {
                    continue;
                }

                ReadDetails(pid, out string imagePath, out DateTimeOffset? startTime);
                return new ProcessTableEntry(
                    pid,
                    (int)entry.th32ParentProcessID,
                    entry.szExeFile,
                    imagePath,
                    startTime
                );
            } while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return null;
    }

    /// <summary>
    /// Terminates <paramref name="entry"/> only while its pid still has the start time the
    /// snapshot saw, so a reused pid is never killed.
    /// </summary>
    public static ProcessKillResult Kill(ProcessTableEntry entry)
    {
        if (entry == null || entry.StartTime == null)
        {
            return ProcessKillResult.Failed;
        }

        IntPtr process = OpenProcess(
            ProcessTerminate | ProcessQueryLimitedInformation,
            false,
            (uint)entry.Pid
        );
        if (process == IntPtr.Zero)
        {
            return Marshal.GetLastWin32Error() switch
            {
                ErrorAccessDenied => ProcessKillResult.AccessDenied,
                ErrorInvalidParameter => ProcessKillResult.Gone,
                _ => ProcessKillResult.Failed,
            };
        }

        try
        {
            if (StartTimeOf(process) != entry.StartTime)
            {
                return ProcessKillResult.Replaced;
            }

            if (TerminateProcess(process, 1))
            {
                return ProcessKillResult.Killed;
            }

            return Marshal.GetLastWin32Error() == ErrorAccessDenied
                ? ProcessKillResult.AccessDenied
                : ProcessKillResult.Failed;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static void ReadDetails(int pid, out string imagePath, out DateTimeOffset? startTime)
    {
        imagePath = null;
        startTime = null;
        if (pid <= 4)
        {
            return;
        }

        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (process == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            if (QueryFullProcessImageName(process, 0, buffer, ref size))
            {
                imagePath = buffer.ToString(0, size);
            }

            startTime = StartTimeOf(process);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static DateTimeOffset? StartTimeOf(IntPtr process)
    {
        if (!GetProcessTimes(process, out long creation, out _, out _, out _) || creation <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromFileTime(creation).ToUniversalTime();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport(
        "kernel32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        EntryPoint = "Process32FirstW"
    )]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport(
        "kernel32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        EntryPoint = "Process32NextW"
    )]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport(
        "kernel32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        EntryPoint = "QueryFullProcessImageNameW"
    )]
    private static extern bool QueryFullProcessImageName(
        IntPtr process,
        int flags,
        StringBuilder name,
        ref int size
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(
        IntPtr process,
        out long creation,
        out long exit,
        out long kernel,
        out long user
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);
}
