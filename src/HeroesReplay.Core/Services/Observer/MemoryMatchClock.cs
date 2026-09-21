using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// Read-only scan of HeroesOfTheStorm_x64 for a live match clock.
/// After HUD OCR provides a time, candidate addresses that keep step
/// with that time are locked. No writes, no injection.
/// </summary>
public sealed class MemoryMatchClock
{
    private readonly ILogger logger;
    private readonly List<Candidate> candidates = new();
    private Candidate? locked;
    private int agreeCount;
    private int lastOcrSeconds = -1;
    private bool scanned;

    public MemoryMatchClock(ILogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public TimeSpan? LastRead { get; private set; }

    public bool IsLocked => locked != null;

    public void Reset()
    {
        candidates.Clear();
        locked = null;
        agreeCount = 0;
        lastOcrSeconds = -1;
        scanned = false;
        LastRead = null;
    }

    public void Observe(Process process, TimeSpan ocrReplayTime)
    {
        if (process == null || process.HasExited)
        {
            return;
        }

        int seconds = (int)Math.Floor(ocrReplayTime.TotalSeconds);
        if (seconds < 0 || seconds > 4 * 60 * 60)
        {
            return;
        }

        if (locked != null)
        {
            if (TryRead(process, locked.Value, out int value))
            {
                LastRead = TimeSpan.FromSeconds(value);
                int delta = Math.Abs(value - seconds);
                if (delta <= 2)
                {
                    agreeCount++;
                }
                else
                {
                    logger.LogWarning(
                        "Memory timer {Memory} disagrees with HUD {Hud}; unlocking.",
                        LastRead,
                        ocrReplayTime
                    );
                    locked = null;
                    agreeCount = 0;
                    scanned = false;
                    candidates.Clear();
                }
            }
            else
            {
                locked = null;
                scanned = false;
            }

            return;
        }

        if (!scanned)
        {
            Scan(process, seconds);
            scanned = true;
            lastOcrSeconds = seconds;
            return;
        }

        Filter(process, seconds);
        lastOcrSeconds = seconds;

        if (candidates.Count == 1)
        {
            locked = candidates[0];
            logger.LogInformation(
                "Locked memory timer at 0x{Address:X} ({Kind}).",
                locked.Value.Address.ToInt64(),
                locked.Value.Kind
            );
        }
    }

    public TimeSpan? TryRead(Process process)
    {
        if (process == null || locked == null)
        {
            return LastRead;
        }

        if (TryRead(process, locked.Value, out int value))
        {
            LastRead = TimeSpan.FromSeconds(value);
            return LastRead;
        }

        return LastRead;
    }

    private void Scan(Process process, int seconds)
    {
        candidates.Clear();
        IntPtr handle = Native.OpenProcess(
            Native.ProcessVmRead | Native.ProcessQueryInformation,
            false,
            process.Id
        );
        if (handle == IntPtr.Zero)
        {
            logger.LogWarning("OpenProcess VM_READ failed for pid {Pid}.", process.Id);
            return;
        }

        try
        {
            byte[] intNeedle = BitConverter.GetBytes(seconds);
            byte[] floatNeedle = BitConverter.GetBytes((float)seconds);
            long address = 0;
            const int maxCandidates = 128;
            while (address < 0x00007FFFFFFFFFFF && candidates.Count < maxCandidates)
            {
                if (
                    Native.VirtualQueryEx(
                        handle,
                        (IntPtr)address,
                        out Native.MemoryBasicInformation mbi,
                        Native.MbiSize
                    ) == 0
                )
                {
                    break;
                }

                long size = (long)mbi.RegionSize;
                if (size <= 0)
                {
                    break;
                }

                if (
                    mbi.State == Native.MemCommit
                    && (mbi.Protect & Native.PageGuard) == 0
                    && (mbi.Protect & Native.PageNoAccess) == 0
                    && IsReadable(mbi.Protect)
                )
                {
                    ScanRegion(
                        handle,
                        (long)mbi.BaseAddress,
                        size,
                        intNeedle,
                        floatNeedle,
                        seconds
                    );
                }

                long next = (long)mbi.BaseAddress + size;
                if (next <= address)
                {
                    break;
                }

                address = next;
            }
        }
        finally
        {
            Native.CloseHandle(handle);
        }

        logger.LogInformation(
            "Memory timer scan for {Seconds}s found {Count} candidates.",
            seconds,
            candidates.Count
        );
    }

    private void ScanRegion(
        IntPtr handle,
        long baseAddress,
        long size,
        byte[] intNeedle,
        byte[] floatNeedle,
        int seconds
    )
    {
        const int chunk = 64 * 1024;
        byte[] buffer = new byte[chunk];
        for (long offset = 0; offset + 4 <= size && candidates.Count < 128; offset += chunk)
        {
            int toRead = (int)Math.Min(chunk, size - offset);
            if (
                !Native.ReadProcessMemory(
                    handle,
                    (IntPtr)(baseAddress + offset),
                    buffer,
                    toRead,
                    out int read
                )
                || read < 4
            )
            {
                continue;
            }

            for (int i = 0; i + 4 <= read && candidates.Count < 128; i += 4)
            {
                if (Matches(buffer, i, intNeedle))
                {
                    candidates.Add(
                        new Candidate((IntPtr)(baseAddress + offset + i), Kind.Int32Seconds)
                    );
                }
                else if (Matches(buffer, i, floatNeedle) && seconds > 10)
                {
                    candidates.Add(
                        new Candidate((IntPtr)(baseAddress + offset + i), Kind.FloatSeconds)
                    );
                }
            }
        }
    }

    private void Filter(Process process, int seconds)
    {
        IntPtr handle = Native.OpenProcess(
            Native.ProcessVmRead | Native.ProcessQueryInformation,
            false,
            process.Id
        );
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var kept = new List<Candidate>();
            foreach (Candidate candidate in candidates)
            {
                if (!TryRead(handle, candidate, out int value))
                {
                    continue;
                }

                int expectedDelta = seconds - lastOcrSeconds;
                int actualDelta = value - lastOcrSeconds;
                if (Math.Abs(actualDelta - expectedDelta) <= 1 && Math.Abs(value - seconds) <= 2)
                {
                    kept.Add(candidate);
                }
            }

            candidates.Clear();
            candidates.AddRange(kept);
            logger.LogDebug("Memory timer candidates remaining: {Count}.", candidates.Count);
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private static bool TryRead(Process process, Candidate candidate, out int seconds)
    {
        seconds = 0;
        IntPtr handle = Native.OpenProcess(
            Native.ProcessVmRead | Native.ProcessQueryInformation,
            false,
            process.Id
        );
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return TryRead(handle, candidate, out seconds);
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private static bool TryRead(IntPtr handle, Candidate candidate, out int seconds)
    {
        seconds = 0;
        byte[] buffer = new byte[4];
        if (
            !Native.ReadProcessMemory(handle, candidate.Address, buffer, 4, out int read)
            || read != 4
        )
        {
            return false;
        }

        if (candidate.Kind == Kind.Int32Seconds)
        {
            seconds = BitConverter.ToInt32(buffer, 0);
            return seconds >= 0 && seconds <= 4 * 60 * 60;
        }

        float f = BitConverter.ToSingle(buffer, 0);
        if (float.IsNaN(f) || f < 0 || f > 4 * 60 * 60)
        {
            return false;
        }

        seconds = (int)Math.Floor(f);
        return true;
    }

    private static bool Matches(byte[] buffer, int index, byte[] needle)
    {
        return buffer[index] == needle[0]
            && buffer[index + 1] == needle[1]
            && buffer[index + 2] == needle[2]
            && buffer[index + 3] == needle[3];
    }

    private static bool IsReadable(uint protect)
    {
        return protect == Native.PageReadonly
            || protect == Native.PageReadWrite
            || protect == Native.PageWriteCopy
            || protect == Native.PageExecuteRead
            || protect == Native.PageExecuteReadWrite;
    }

    private enum Kind
    {
        Int32Seconds,
        FloatSeconds,
    }

    private readonly struct Candidate
    {
        public Candidate(IntPtr address, Kind kind)
        {
            Address = address;
            Kind = kind;
        }

        public IntPtr Address { get; }
        public Kind Kind { get; }
    }

    private static class Native
    {
        public const int ProcessVmRead = 0x0010;
        public const int ProcessQueryInformation = 0x0400;
        public const uint MemCommit = 0x1000;
        public const uint PageNoAccess = 0x01;
        public const uint PageReadonly = 0x02;
        public const uint PageReadWrite = 0x04;
        public const uint PageWriteCopy = 0x08;
        public const uint PageExecuteRead = 0x20;
        public const uint PageExecuteReadWrite = 0x40;
        public const uint PageGuard = 0x100;
        public static readonly int MbiSize = Marshal.SizeOf<MemoryBasicInformation>();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(
            int dwDesiredAccess,
            bool bInheritHandle,
            int dwProcessId
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            int dwSize,
            out int lpNumberOfBytesRead
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int VirtualQueryEx(
            IntPtr hProcess,
            IntPtr lpAddress,
            out MemoryBasicInformation lpBuffer,
            int dwLength
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        public struct MemoryBasicInformation
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public ushort PartitionId;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }
    }
}
