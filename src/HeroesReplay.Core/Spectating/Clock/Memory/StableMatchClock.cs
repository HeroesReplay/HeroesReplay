using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HeroesReplay.Core.Spectating.Clock.Memory;

public readonly record struct StableClockSample(
    bool Ok,
    string Reason,
    int Ticks,
    float Scale,
    double Seconds
);

internal readonly record struct StableClockModule(
    int ProcessId,
    long BaseAddress,
    long Size,
    string FileVersion
);

/// <summary>
/// Read-only match clock. Pattern discovery runs once per process module on every client build.
/// Fixed 98025 RVAs are only a candidate; a failed check stays unlocked and reports no clock.
/// This is the only match clock. The HUD timer is never cropped or OCR'd.
/// </summary>
public sealed class StableMatchClock : IDisposable
{
    private const double MaxCoherentStepSeconds = 8;
    private static readonly TimeSpan RediscoverAfter = TimeSpan.FromSeconds(10);

    private IntPtr handle;
    private int attachedPid;
    private bool fingerprintSet;
    private int pid;
    private long moduleBase;
    private long moduleSize;
    private string version = "";
    private long tickRva;
    private long speedRva;
    private bool discovered;
    private bool located;
    private bool hasSample;
    private int lastTicks;
    private float lastScale;
    private double lastOkSeconds = double.NaN;
    private DateTimeOffset lastOkChange;
    private DateTimeOffset rediscoverAt;
    private string attachReason = "no-process";
    private ClockTelemetryReport lastReport;
    private int telemetryEmissions;

    internal ClockTelemetryReport LastTelemetry { get; private set; }

    internal ClockTelemetryReport DiscoveryTelemetry { get; private set; }

    internal int TelemetryEmissions => telemetryEmissions;

    internal Func<DateTimeOffset> UtcNow { get; set; } = () => DateTimeOffset.UtcNow;

    internal bool IsLocked => located;

    internal long CandidateTickRva => tickRva;

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
        if (!TryAttach(process, out StableClockModule module))
        {
            return new StableClockSample(false, attachReason, 0, 0, 0);
        }

        return Read(module, ReadProcess);
    }

    internal StableClockSample Read(StableClockModule module, Func<long, byte[], bool> read)
    {
        if (module.ProcessId <= 0)
        {
            return Finish(new StableClockSample(false, "no-process", 0, 0, 0));
        }

        if (module.BaseAddress <= 0 || module.Size <= 0)
        {
            return Finish(new StableClockSample(false, "no-module", 0, 0, 0));
        }

        if (read == null)
        {
            return Finish(new StableClockSample(false, "read-failed", 0, 0, 0));
        }

        UseModule(module);
        if (!discovered || (tickRva == 0 && UtcNow() >= rediscoverAt))
        {
            ReportTelemetry(attachReason);
            Discover(read);
        }

        if (tickRva == 0 || speedRva == 0)
        {
            return Finish(new StableClockSample(false, attachReason, 0, 0, 0));
        }

        int ticks = 0;
        float speed = 0;
        if (
            !TryReadInt32(read, moduleBase + tickRva, out ticks)
            || !TryReadSingle(read, moduleBase + speedRva, out speed)
        )
        {
            return Finish(new StableClockSample(false, "read-failed", ticks, speed, 0));
        }

        if (!MatchTickClock.TrySeconds(ticks, speed, out double seconds))
        {
            hasSample = false;
            return Finish(new StableClockSample(false, "bad-scale", ticks, speed, seconds));
        }

        // Zero is also the menu and the loading screen, so it is not a started match.
        if (Math.Abs(seconds) < 0.5)
        {
            return Finish(new StableClockSample(false, "near-zero", ticks, speed, seconds));
        }

        if (!located && !TryConfirm(ticks, speed, seconds, out string reason))
        {
            return Finish(new StableClockSample(false, reason, ticks, speed, seconds));
        }

        // A clock that went back is a new match in the same client, not a frozen cell. Without
        // this, the previous match's last second stayed the baseline and every read was stalled.
        if (StartedOver(lastOkSeconds, seconds))
        {
            BeginMatch();
        }

        if (SameCellIsStale(lastOkSeconds, seconds, lastOkChange, UtcNow()))
        {
            return Finish(new StableClockSample(false, "stalled", ticks, speed, seconds));
        }

        if (double.IsNaN(lastOkSeconds) || seconds > lastOkSeconds + 0.25)
        {
            lastOkSeconds = seconds;
            lastOkChange = UtcNow();
        }

        return Finish(new StableClockSample(true, "ok", ticks, speed, seconds));
    }

    private StableClockSample Finish(StableClockSample sample)
    {
        ReportTelemetry(sample.Reason);
        return sample;
    }

    private void ReportTelemetry(string reason)
    {
        ClockTelemetryReport report = ClockTelemetry.Describe(discovered, located, reason);
        if (!discovered)
        {
            DiscoveryTelemetry = report;
        }

        if (ClockTelemetry.Changed(lastReport, report))
        {
            lastReport = report;
            telemetryEmissions++;
        }

        LastTelemetry = report;
    }

    /// <summary>
    /// Forgets the stall baseline for a new replay. The located clock address is kept.
    /// </summary>
    public void BeginMatch()
    {
        lastOkSeconds = double.NaN;
        lastOkChange = default;
    }

    /// <summary>
    /// Gap between the two reads that prove the clock is moving. The clock counts ticks / 4096
    /// per second, so a running match is about a quarter second ahead on the second read.
    /// </summary>
    public static readonly TimeSpan RunningProbe = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// A match is running only when both reads succeed and the second is ahead of the first.
    /// The menu's zero does not read, and a frozen clock from the last match does not move.
    /// </summary>
    public static bool IsRunning(TimeSpan? first, TimeSpan? second)
    {
        return first.HasValue && second.HasValue && second.Value > first.Value;
    }

    public static bool StartedOver(double previousSeconds, double seconds)
    {
        return !double.IsNaN(previousSeconds) && seconds < previousSeconds - 5;
    }

    /// <summary>
    /// A locked cell that stops moving is not a running match: it is paused, over, or not the clock.
    /// </summary>
    public static bool SameCellIsStale(
        double previousSeconds,
        double seconds,
        DateTimeOffset changedAt,
        DateTimeOffset now
    )
    {
        if (double.IsNaN(previousSeconds) || changedAt == default)
        {
            return false;
        }

        if (seconds > previousSeconds + 0.25)
        {
            return false;
        }

        return now - changedAt >= TimeSpan.FromSeconds(8);
    }

    public void Dispose()
    {
        ReleaseHandle();
        ResetState();
    }

    private void UseModule(StableClockModule module)
    {
        string fileVersion = module.FileVersion ?? "";
        if (
            fingerprintSet
            && pid == module.ProcessId
            && moduleBase == module.BaseAddress
            && moduleSize == module.Size
            && string.Equals(version, fileVersion, StringComparison.Ordinal)
        )
        {
            return;
        }

        fingerprintSet = true;
        pid = module.ProcessId;
        moduleBase = module.BaseAddress;
        moduleSize = module.Size;
        version = fileVersion;
        discovered = false;
        rediscoverAt = default;
        located = false;
        lastReport = default;
        DiscoveryTelemetry = default;
        tickRva = 0;
        speedRva = 0;
        hasSample = false;
        lastTicks = 0;
        lastScale = 0;
        BeginMatch();
    }

    private void Discover(Func<long, byte[], bool> read)
    {
        discovered = true;
        // A client still unpacking its code has no pattern yet. A miss is scanned again later.
        rediscoverAt = UtcNow() + RediscoverAfter;
        located = false;
        tickRva = 0;
        speedRva = 0;
        hasSample = false;
        lastTicks = 0;
        lastScale = 0;

        bool agreed = TryLocateByPattern(
            read,
            out int sites,
            out long patternTick,
            out long patternSpeed
        );
        if (agreed && InRange(patternTick) && InRange(patternSpeed))
        {
            tickRva = patternTick;
            speedRva = patternSpeed;
            attachReason = "pattern";
            return;
        }

        if (
            MatchTickClock.IsSupportedVersion(version)
            && InRange(MatchTickClock.MatchTickRva)
            && InRange(MatchTickClock.GameSpeedFactorRva)
        )
        {
            tickRva = MatchTickClock.MatchTickRva;
            speedRva = MatchTickClock.GameSpeedFactorRva;
            attachReason = "fixed";
            return;
        }

        if (patternTick != 0 || patternSpeed != 0 || MatchTickClock.IsSupportedVersion(version))
        {
            attachReason = "out-of-range";
            return;
        }

        attachReason = sites == 0 ? "unsupported-build" : "pattern-disagreed";
    }

    private bool InRange(long rva)
    {
        return rva > 0 && rva <= moduleSize - 4;
    }

    private bool TryConfirm(int ticks, float scale, double seconds, out string reason)
    {
        if (!hasSample)
        {
            hasSample = true;
            lastTicks = ticks;
            lastScale = scale;
            reason = "confirming";
            return false;
        }

        double delta = seconds - (lastTicks * (double)lastScale);
        bool sameScale = SameScale(scale, lastScale);
        lastTicks = ticks;
        lastScale = scale;
        if (!sameScale || delta < 0 || delta > MaxCoherentStepSeconds)
        {
            reason = "incoherent";
            return false;
        }

        if (delta == 0)
        {
            reason = "confirming";
            return false;
        }

        located = true;
        reason = "ok";
        return true;
    }

    private static bool SameScale(float left, float right)
    {
        return Math.Abs(left - right) <= 0.000001f;
    }

    private bool TryAttach(Process process, out StableClockModule module)
    {
        module = default;
        if (process == null)
        {
            attachReason = "no-process";
            return false;
        }

        int nextPid;
        try
        {
            if (process.HasExited)
            {
                attachReason = "no-process";
                return false;
            }

            nextPid = process.Id;
        }
        catch
        {
            attachReason = "no-process";
            return false;
        }

        if (handle == IntPtr.Zero || attachedPid != nextPid)
        {
            ReleaseHandle();
            handle = Native.OpenProcess(
                Native.ProcessQueryInformation | Native.ProcessVmRead,
                false,
                nextPid
            );
            if (handle == IntPtr.Zero)
            {
                attachReason = "open-failed";
                return false;
            }

            attachedPid = nextPid;
        }

        try
        {
            ProcessModule main = process.MainModule;
            long baseAddress = main?.BaseAddress.ToInt64() ?? 0;
            long size = main?.ModuleMemorySize ?? 0;
            string fileVersion = main?.FileVersionInfo.FileVersion;
            if (baseAddress == 0 || size <= 0)
            {
                attachReason = "no-module";
                return false;
            }

            module = new StableClockModule(nextPid, baseAddress, size, fileVersion);
            return true;
        }
        catch
        {
            attachReason = "unsupported-build";
            return false;
        }
    }

    private bool TryLocateByPattern(
        Func<long, byte[], bool> read,
        out int sites,
        out long patternTick,
        out long patternSpeed
    )
    {
        sites = 0;
        patternTick = 0;
        patternSpeed = 0;
        byte[] headers = new byte[0x1000];
        if (
            !TryRead(read, moduleBase, headers)
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

            CollectSites(read, moduleBase, section.VirtualAddress, section.VirtualSize, found);
        }

        sites = found.Count;
        return MatchClockPattern.TryAgree(found, out patternTick, out patternSpeed);
    }

    private static void CollectSites(
        Func<long, byte[], bool> read,
        long moduleBase,
        long rva,
        int size,
        List<MatchClockPattern.Site> found
    )
    {
        if (size <= 0 || size > 64 * 1024 * 1024)
        {
            return;
        }

        byte[] window = new byte[size];
        if (TryRead(read, moduleBase + rva, window))
        {
            found.AddRange(MatchClockPattern.Find(window, rva));
            return;
        }

        const int chunk = 1 << 20;
        for (int offset = 0; offset < size; offset += chunk)
        {
            int count = Math.Min(size - offset, chunk + MatchClockPattern.MulssEnd);
            byte[] slice = new byte[count];
            if (!TryRead(read, moduleBase + rva + offset, slice))
            {
                continue;
            }

            found.AddRange(MatchClockPattern.Find(slice, rva + offset));
        }
    }

    private static bool TryReadInt32(Func<long, byte[], bool> read, long address, out int value)
    {
        value = 0;
        byte[] buffer = new byte[4];
        if (!TryRead(read, address, buffer))
        {
            return false;
        }

        value = BitConverter.ToInt32(buffer, 0);
        return true;
    }

    private static bool TryReadSingle(Func<long, byte[], bool> read, long address, out float value)
    {
        value = 0;
        byte[] buffer = new byte[4];
        if (!TryRead(read, address, buffer))
        {
            return false;
        }

        value = BitConverter.ToSingle(buffer, 0);
        return true;
    }

    private static bool TryRead(Func<long, byte[], bool> read, long address, byte[] buffer)
    {
        return read != null
            && address > 0
            && buffer != null
            && buffer.Length > 0
            && read(address, buffer);
    }

    private bool ReadProcess(long address, byte[] buffer)
    {
        return handle != IntPtr.Zero
            && address > 0
            && buffer != null
            && buffer.Length > 0
            && Native.ReadProcessMemory(
                handle,
                (IntPtr)address,
                buffer,
                buffer.Length,
                out int read
            )
            && read == buffer.Length;
    }

    private void ReleaseHandle()
    {
        if (handle != IntPtr.Zero)
        {
            Native.CloseHandle(handle);
            handle = IntPtr.Zero;
        }

        attachedPid = 0;
    }

    private void ResetState()
    {
        fingerprintSet = false;
        pid = 0;
        moduleBase = 0;
        moduleSize = 0;
        version = "";
        tickRva = 0;
        speedRva = 0;
        discovered = false;
        rediscoverAt = default;
        located = false;
        hasSample = false;
        lastTicks = 0;
        lastScale = 0;
        lastOkSeconds = double.NaN;
        lastOkChange = default;
        attachReason = "no-process";
        lastReport = default;
        DiscoveryTelemetry = default;
        LastTelemetry = default;
        telemetryEmissions = 0;
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
