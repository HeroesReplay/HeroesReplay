using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// Reads the fixed 98025 match-tick RVAs. No scanning and no writes.
/// </summary>
public readonly record struct StableClockSample(
    bool Ok,
    string Reason,
    int Ticks,
    float Scale,
    double Seconds
);

public sealed class StableMatchClock : IDisposable
{
    private IntPtr handle;
    private int pid;
    private long moduleBase;
    private long moduleSize;
    private long tickRva;
    private long speedRva;
    private bool located;
    private string attachReason = "no-process";

    public bool TryRead(Process process, out TimeSpan time)
    {
        StableClockSample sample = Read(process);
        if (!sample.Ok)
        {
            time = default;
            return false;
        }

        time = TimeSpan.FromSeconds(sample.Seconds);
        return true;
    }

    public StableClockSample Read(Process process)
    {
        if (!Ensure(process))
        {
            return new StableClockSample(false, attachReason, 0, 0, 0);
        }

        int ticks = 0;
        float speed = 0;
        if (
            !TryReadInt32(moduleBase + tickRva, out ticks)
            || !TryReadSingle(moduleBase + speedRva, out speed)
        )
        {
            return new StableClockSample(false, "read-failed", ticks, speed, 0);
        }

        if (!MatchTickClock.TrySeconds(ticks, speed, out double seconds))
        {
            return new StableClockSample(false, "bad-scale", ticks, speed, seconds);
        }

        // Zero is also the menu. Leave that second to OCR so a dead read cannot freeze the clock.
        if (Math.Abs(seconds) < 0.5)
        {
            return new StableClockSample(false, "near-zero", ticks, speed, seconds);
        }

        return new StableClockSample(true, "ok", ticks, speed, seconds);
    }

    public void Dispose()
    {
        Close();
    }

    private bool Ensure(Process process)
    {
        if (process == null)
        {
            return false;
        }

        int nextPid;
        try
        {
            if (process.HasExited)
            {
                return false;
            }

            nextPid = process.Id;
        }
        catch
        {
            return false;
        }

        if (handle != IntPtr.Zero && pid == nextPid && located && moduleBase != 0)
        {
            return true;
        }

        Close();
        pid = nextPid;
        handle = Native.OpenProcess(
            Native.ProcessQueryInformation | Native.ProcessVmRead,
            false,
            pid
        );
        if (handle == IntPtr.Zero)
        {
            attachReason = "open-failed";
            return false;
        }

        try
        {
            ProcessModule module = process.MainModule;
            moduleBase = module?.BaseAddress.ToInt64() ?? 0;
            moduleSize = module?.ModuleMemorySize ?? 0;
            string version = module?.FileVersionInfo.FileVersion;
            if (moduleBase == 0)
            {
                attachReason = "no-module";
                return false;
            }

            if (MatchTickClock.IsSupportedVersion(version))
            {
                tickRva = MatchTickClock.MatchTickRva;
                speedRva = MatchTickClock.GameSpeedFactorRva;
                located = true;
                attachReason = "ok";
                return true;
            }

            if (TryLocateByPattern(out int sites))
            {
                located = true;
                attachReason = "pattern";
                return true;
            }

            attachReason = sites == 0 ? "unsupported-build" : "pattern-disagreed";
            return false;
        }
        catch
        {
            located = false;
            moduleBase = 0;
            attachReason = "unsupported-build";
            return false;
        }
    }

    private bool TryLocateByPattern(out int sites)
    {
        sites = 0;
        tickRva = 0;
        speedRva = 0;
        byte[] headers = new byte[0x1000];
        if (
            !TryRead(moduleBase, headers)
            || !MatchClockPattern.TryExecutableSections(headers, out var sections)
        )
        {
            return false;
        }

        var found = new List<MatchClockPattern.Site>();
        foreach (MatchClockPattern.Section section in sections)
        {
            if (section.VirtualSize <= 0 || section.VirtualAddress < 0)
            {
                continue;
            }

            if (moduleSize > 0 && section.VirtualAddress + section.VirtualSize > moduleSize)
            {
                continue;
            }

            CollectSites(section.VirtualAddress, section.VirtualSize, found);
        }

        sites = found.Count;
        return MatchClockPattern.TryAgree(found, out tickRva, out speedRva);
    }

    private void CollectSites(long rva, int size, List<MatchClockPattern.Site> found)
    {
        if (size <= 0 || size > 64 * 1024 * 1024)
        {
            return;
        }

        byte[] window = new byte[size];
        if (TryRead(moduleBase + rva, window))
        {
            found.AddRange(MatchClockPattern.Find(window, rva));
            return;
        }

        const int chunk = 1 << 20;
        for (int offset = 0; offset < size; offset += chunk)
        {
            int count = Math.Min(size - offset, chunk + MatchClockPattern.MulssEnd);
            byte[] slice = new byte[count];
            if (!TryRead(moduleBase + rva + offset, slice))
            {
                continue;
            }

            found.AddRange(MatchClockPattern.Find(slice, rva + offset));
        }
    }

    private bool TryReadInt32(long address, out int value)
    {
        value = 0;
        byte[] buffer = new byte[4];
        if (!TryRead(address, buffer))
        {
            return false;
        }

        value = BitConverter.ToInt32(buffer, 0);
        return true;
    }

    private bool TryReadSingle(long address, out float value)
    {
        value = 0;
        byte[] buffer = new byte[4];
        if (!TryRead(address, buffer))
        {
            return false;
        }

        value = BitConverter.ToSingle(buffer, 0);
        return true;
    }

    private bool TryRead(long address, byte[] buffer)
    {
        return handle != IntPtr.Zero
            && address > 0
            && Native.ReadProcessMemory(
                handle,
                (IntPtr)address,
                buffer,
                buffer.Length,
                out int read
            )
            && read == buffer.Length;
    }

    private void Close()
    {
        if (handle != IntPtr.Zero)
        {
            Native.CloseHandle(handle);
            handle = IntPtr.Zero;
        }

        pid = 0;
        moduleBase = 0;
        moduleSize = 0;
        tickRva = 0;
        speedRva = 0;
        located = false;
    }

    private static class Native
    {
        public const int ProcessVmRead = 0x0010;
        public const int ProcessQueryInformation = 0x0400;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(int access, bool inherit, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(
            IntPtr process,
            IntPtr address,
            byte[] buffer,
            int size,
            out int read
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
