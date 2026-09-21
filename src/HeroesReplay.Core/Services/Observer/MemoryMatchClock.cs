using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// Read-only scan of HeroesOfTheStorm_x64 for a live match clock.
/// Discovery is time-boxed and limited to private writable pages. Float values
/// that are not whole numbers are tried first. Slabs with many copies of the
/// same second are skipped. Candidates that do not tick with the HUD are dropped.
/// After one address locks, only that address is read. No writes and no injection.
/// </summary>
public sealed class MemoryMatchClock
{
    public static readonly TimeSpan DefaultScanBudget = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan MaxScanBudget = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RescanCooldown = TimeSpan.FromSeconds(30);
    private const long UserSpaceLimit = 0x00007FFFFFFFFFFF;
    private const int ChunkBytes = 256 * 1024;
    private const long ProgressBytes = 64L * 1024 * 1024;

    private readonly ILogger logger;
    private readonly List<MemoryTimerCandidate> candidates = new();
    private readonly HashSet<long> seen = new();
    private readonly byte[] buffer = new byte[ChunkBytes];

    private MemoryTimerCandidate? locked;
    private MemoryTimerKind mode = MemoryTimerKind.FloatSeconds;
    private int watchedPid;
    private int lastHud = -1;
    private int lockMisses;
    private int pausedAtHud = -1;
    private long cursor;
    private long totalBytes;
    private long lastLoggedBand = -1;
    private int noisyRegions;
    private bool discoveryComplete;
    private bool discoveryPaused;
    private bool announcedPass;
    private bool holdingLogged;
    private DateTime cooldownUntil = DateTime.MinValue;

    public MemoryMatchClock(ILogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public TimeSpan? LastRead { get; private set; }

    public bool IsLocked => locked.HasValue;

    public int CandidateCount => candidates.Count;

    public string Phase
    {
        get
        {
            if (locked.HasValue)
            {
                return "locked";
            }

            if (cooldownUntil > DateTime.UtcNow)
            {
                return "cooldown";
            }

            if (!discoveryComplete)
            {
                string name = mode == MemoryTimerKind.Int32Seconds ? "scan-int" : "scan-float";
                return discoveryPaused ? name + "-paused" : name;
            }

            return candidates.Count > 0 ? "filtering" : "idle";
        }
    }

    public void Reset()
    {
        RestartSearch();
        watchedPid = 0;
        lastHud = -1;
        LastRead = null;
    }

    public void Observe(Process process, TimeSpan ocrReplayTime, TimeSpan? scanBudget = null)
    {
        if (!IsAlive(process))
        {
            return;
        }

        int hud = (int)Math.Floor(ocrReplayTime.TotalSeconds);
        if (!MemoryTimerSelection.InRange(hud))
        {
            return;
        }

        if (process.Id != watchedPid)
        {
            if (watchedPid != 0)
            {
                logger.LogInformation(
                    "Memory timer process changed from {OldPid} to {NewPid}; resetting scan.",
                    watchedPid,
                    process.Id
                );
            }

            RestartSearch();
            watchedPid = process.Id;
            lastHud = -1;
        }

        if (locked.HasValue)
        {
            ReadLocked(process, hud);
            lastHud = hud;
            return;
        }

        // Int 0 is packed into zeroed pages and cannot be distinguished from a clock.
        if (hud < 1)
        {
            lastHud = hud;
            return;
        }

        if (cooldownUntil > DateTime.UtcNow)
        {
            return;
        }

        if (cooldownUntil != DateTime.MinValue)
        {
            cooldownUntil = DateTime.MinValue;
            RestartSearch();
            logger.LogInformation(
                "Memory timer cooldown elapsed; scanning again from HUD {Hud}s.",
                hud
            );
        }

        // One slice per new HUD second. A stuck OCR clock must not walk memory every tick.
        if (hud == lastHud)
        {
            if (!holdingLogged)
            {
                holdingLogged = true;
                logger.LogInformation(
                    "Memory timer holding at HUD {Hud}s ({Phase}, {Count} candidates) until that second changes.",
                    hud,
                    Phase,
                    candidates.Count
                );
            }

            return;
        }

        holdingLogged = false;

        TimeSpan budget = NormalizeBudget(scanBudget);
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
            if (candidates.Count > 0 && lastHud >= 0)
            {
                FilterCandidates(handle, hud);
            }

            if (!discoveryComplete && !discoveryPaused)
            {
                ScanSlice(handle, hud, budget);
            }

            if (discoveryComplete && candidates.Count == 0 && !locked.HasValue)
            {
                AdvanceOrCooldown(hud);
            }

            TryApplyLock(hud);
        }
        finally
        {
            Native.CloseHandle(handle);
        }

        lastHud = hud;
    }

    public TimeSpan? TryRead(Process process)
    {
        if (!IsAlive(process) || !locked.HasValue)
        {
            return LastRead;
        }

        IntPtr handle = Native.OpenProcess(
            Native.ProcessVmRead | Native.ProcessQueryInformation,
            false,
            process.Id
        );
        if (handle == IntPtr.Zero)
        {
            return LastRead;
        }

        try
        {
            if (TryRead(handle, locked.Value, out int value))
            {
                LastRead = TimeSpan.FromSeconds(value);
            }
        }
        finally
        {
            Native.CloseHandle(handle);
        }

        return LastRead;
    }

    private void FilterCandidates(IntPtr handle, int hud)
    {
        var readings = new Dictionary<long, int>(candidates.Count);
        foreach (MemoryTimerCandidate candidate in candidates)
        {
            if (TryRead(handle, candidate, out int value))
            {
                readings[candidate.Address] = value;
            }
        }

        List<MemoryTimerCandidate> kept = MemoryTimerSelection.KeepTicking(
            candidates,
            readings,
            lastHud,
            hud
        );
        if (kept.Count != candidates.Count)
        {
            logger.LogInformation(
                "Memory timer candidates {Before} -> {After} (HUD {Previous}s -> {Current}s).",
                candidates.Count,
                kept.Count,
                lastHud,
                hud
            );
        }

        ReplaceCandidates(kept);
        List<MemoryTimerCandidate> ticking = MemoryTimerSelection.Ticking(candidates);
        if (ticking.Count > 0)
        {
            if (ticking.Count != candidates.Count)
            {
                ReplaceCandidates(ticking);
            }

            // Do not scan further. New copies of the current second drown the cells that tick.
            discoveryPaused = false;
            discoveryComplete = true;
            pausedAtHud = -1;
            logger.LogInformation(
                "Memory timer kept {Count} {Mode} candidates that moved with the HUD. Not scanning for more.",
                candidates.Count,
                ModeLabel
            );
            return;
        }

        if (
            discoveryPaused
            && pausedAtHud >= 0
            && hud - pausedAtHud >= 8
            && candidates.Count > MemoryTimerSelection.ResumeBelow
        )
        {
            logger.LogWarning(
                "Memory timer still has {Count} {Mode} candidates after {Seconds}s and none tracked the HUD. Continuing the scan.",
                candidates.Count,
                ModeLabel,
                hud - pausedAtHud
            );
            ReplaceCandidates(Array.Empty<MemoryTimerCandidate>());
            discoveryPaused = false;
            pausedAtHud = -1;
            return;
        }

        if (discoveryPaused && candidates.Count == 0)
        {
            discoveryPaused = false;
            pausedAtHud = -1;
            logger.LogInformation(
                "Memory timer candidates did not tick. Resuming {Mode} scan at 0x{Cursor:X}.",
                ModeLabel,
                cursor
            );
        }
    }

    private void ScanSlice(IntPtr handle, int hud, TimeSpan budget)
    {
        if (Native.MbiSize != 48)
        {
            logger.LogWarning(
                "MEMORY_BASIC_INFORMATION size is {Size}, expected 48. Skipping memory timer scan.",
                Native.MbiSize
            );
            discoveryComplete = true;
            return;
        }

        if (!announcedPass)
        {
            announcedPass = true;
            logger.LogInformation(
                "Memory timer {Mode} scan started at HUD {Hud}s (private writable pages, {Budget}ms this tick, read-only).",
                ModeLabel,
                hud,
                (int)budget.TotalMilliseconds
            );
        }

        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < budget && candidates.Count < MemoryTimerSelection.MaxCandidates)
        {
            if (cursor >= UserSpaceLimit)
            {
                CompletePass(hud);
                return;
            }

            long before = cursor;
            if (
                Native.VirtualQueryEx(
                    handle,
                    (IntPtr)cursor,
                    out Native.MemoryBasicInformation mbi,
                    Native.MbiSize
                ) == 0
            )
            {
                CompletePass(hud);
                return;
            }

            long size = (long)mbi.RegionSize;
            long regionStart = mbi.BaseAddress.ToInt64();
            if (size <= 0 || regionStart + size <= before)
            {
                cursor = before + 0x1000;
                if (cursor <= before)
                {
                    CompletePass(hud);
                    return;
                }

                continue;
            }

            long regionEnd = regionStart + size;
            if (ShouldScan(mbi))
            {
                long pos = Math.Max(cursor, regionStart);
                if (!ScanRange(handle, pos, regionEnd, hud, budget, watch))
                {
                    NoteProgress();
                    return;
                }
            }

            cursor = regionEnd;
        }

        if (candidates.Count >= MemoryTimerSelection.MaxCandidates && !discoveryPaused)
        {
            discoveryPaused = true;
            pausedAtHud = hud;
            logger.LogWarning(
                "Memory timer hit {Cap} {Mode} candidates at 0x{Cursor:X} (HUD {Hud}s). Not locking until they tick with the HUD.",
                MemoryTimerSelection.MaxCandidates,
                ModeLabel,
                cursor,
                hud
            );
        }

        NoteProgress();
    }

    private bool ScanRange(
        IntPtr handle,
        long start,
        long end,
        int hud,
        TimeSpan budget,
        Stopwatch watch
    )
    {
        long pos = start;
        while (pos + 4 <= end)
        {
            if ((pos & 3) != 0)
            {
                pos = (pos + 3) & ~3L;
            }

            if (watch.Elapsed >= budget || candidates.Count >= MemoryTimerSelection.MaxCandidates)
            {
                cursor = pos;
                if (candidates.Count >= MemoryTimerSelection.MaxCandidates && !discoveryPaused)
                {
                    discoveryPaused = true;
                    pausedAtHud = hud;
                    logger.LogWarning(
                        "Memory timer hit {Cap} {Mode} candidates at 0x{Cursor:X} (HUD {Hud}s). Not locking until they tick with the HUD.",
                        MemoryTimerSelection.MaxCandidates,
                        ModeLabel,
                        cursor,
                        hud
                    );
                }

                return false;
            }

            int toRead = (int)Math.Min(ChunkBytes, end - pos);
            if (
                !Native.ReadProcessMemory(handle, (IntPtr)pos, buffer, toRead, out int read)
                || read < 4
            )
            {
                long skip = Math.Max(toRead, 0x1000);
                pos += skip;
                cursor = pos;
                continue;
            }

            bool noisy = ConsiderChunk(pos, read, hud);
            totalBytes += read;
            if (noisy)
            {
                // A slab full of the same small value is entity data, not one clock.
                noisyRegions++;
                cursor = end;
                return true;
            }

            if (pos + read >= end)
            {
                cursor = end;
                return true;
            }

            pos += read;
            cursor = pos;
        }

        cursor = end;
        return true;
    }

    private bool ConsiderChunk(long baseAddress, int read, int hud)
    {
        if ((baseAddress & 3) != 0)
        {
            return false;
        }

        int aligned = read & ~3;
        if (aligned < 4)
        {
            return false;
        }

        int start = candidates.Count;
        ReadOnlySpan<int> values = MemoryMarshal.Cast<byte, int>(buffer.AsSpan(0, aligned));
        for (
            int i = 0;
            i < values.Length && candidates.Count < MemoryTimerSelection.MaxCandidates;
            i++
        )
        {
            int decoded;
            if (mode == MemoryTimerKind.Int32Seconds)
            {
                if (values[i] != hud)
                {
                    continue;
                }

                decoded = values[i];
            }
            else
            {
                float seconds = BitConverter.Int32BitsToSingle(values[i]);
                if (!MemoryTimerSelection.IsFloatClockNeedle(seconds, hud))
                {
                    continue;
                }

                decoded = (int)MathF.Floor(seconds);
                if (!MemoryTimerSelection.InRange(decoded))
                {
                    continue;
                }
            }

            long address = baseAddress + (i * 4L);
            if (!seen.Add(address))
            {
                continue;
            }

            candidates.Add(new MemoryTimerCandidate(address, mode, decoded, 0));
            if (candidates.Count - start > MemoryTimerSelection.MaxHitsPerChunk)
            {
                RollbackChunk(start);
                return true;
            }
        }

        return false;
    }

    private void RollbackChunk(int start)
    {
        for (int i = start; i < candidates.Count; i++)
        {
            seen.Remove(candidates[i].Address);
        }

        candidates.RemoveRange(start, candidates.Count - start);
    }

    private void CompletePass(int hud)
    {
        discoveryComplete = true;
        logger.LogInformation(
            "Memory timer {Mode} scan finished at HUD {Hud}s with {Count} candidates after {Megabytes:F1} MB.",
            ModeLabel,
            hud,
            candidates.Count,
            totalBytes / (1024.0 * 1024.0)
        );
    }

    private void NoteProgress()
    {
        if (totalBytes <= 0)
        {
            return;
        }

        long band = totalBytes / ProgressBytes;
        if (band <= lastLoggedBand)
        {
            return;
        }

        lastLoggedBand = band;
        logger.LogInformation(
            "Memory timer {Mode} scan read {Megabytes:F0} MB, cursor 0x{Cursor:X}, {Count} candidates, {Skipped} noisy regions skipped.",
            ModeLabel,
            totalBytes / (1024.0 * 1024.0),
            cursor,
            candidates.Count,
            noisyRegions
        );
    }

    private void AdvanceOrCooldown(int hud)
    {
        if (mode == MemoryTimerKind.FloatSeconds)
        {
            mode = MemoryTimerKind.Int32Seconds;
            cursor = 0;
            totalBytes = 0;
            lastLoggedBand = -1;
            noisyRegions = 0;
            discoveryComplete = false;
            discoveryPaused = false;
            pausedAtHud = -1;
            announcedPass = false;
            seen.Clear();
            logger.LogInformation(
                "Memory timer float scan found no clock. Starting int scan at HUD {Hud}s.",
                hud
            );
            return;
        }

        cooldownUntil = DateTime.UtcNow.Add(RescanCooldown);
        logger.LogInformation(
            "Memory timer found no ticking clock at HUD {Hud}s. Next scan in {Seconds:0}s. HUD OCR stays the clock.",
            hud,
            RescanCooldown.TotalSeconds
        );
    }

    private void TryApplyLock(int hud)
    {
        MemoryTimerCandidate? chosen = MemoryTimerSelection.TryLock(
            candidates,
            discoveryComplete,
            discoveryPaused
        );
        if (chosen == null)
        {
            return;
        }

        locked = chosen;
        LastRead = TimeSpan.FromSeconds(chosen.Value.Seconds);
        logger.LogInformation(
            "Locked memory timer at 0x{Address:X} ({Kind}) after {Ticks} ticks at HUD {Hud}s. {Copies} candidate(s) agreed. Later reads use only this address.",
            chosen.Value.Address,
            chosen.Value.Kind,
            chosen.Value.AgreeTicks,
            hud,
            candidates.Count
        );
        ReplaceCandidates(Array.Empty<MemoryTimerCandidate>());
    }

    private void ReadLocked(Process process, int hud)
    {
        IntPtr handle = Native.OpenProcess(
            Native.ProcessVmRead | Native.ProcessQueryInformation,
            false,
            process.Id
        );
        if (handle == IntPtr.Zero)
        {
            NoteLockMiss("OpenProcess failed");
            return;
        }

        try
        {
            if (!TryRead(handle, locked.Value, out int value))
            {
                NoteLockMiss("read failed");
                return;
            }

            LastRead = TimeSpan.FromSeconds(value);
            bool tracks =
                lastHud < 0
                || MemoryTimerSelection.TracksHud(locked.Value.Seconds, value, lastHud, hud);
            if (!tracks)
            {
                NoteLockMiss($"read {value}s vs HUD {hud}s");
                return;
            }

            lockMisses = 0;
            locked = locked.Value with
            {
                Seconds = value,
                AgreeTicks = locked.Value.AgreeTicks + 1,
            };
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private void NoteLockMiss(string reason)
    {
        if (!locked.HasValue)
        {
            return;
        }

        lockMisses++;
        logger.LogWarning(
            "Memory timer at 0x{Address:X} missed ({Miss}/2): {Reason}.",
            locked.Value.Address,
            lockMisses,
            reason
        );
        if (lockMisses < 2)
        {
            return;
        }

        logger.LogWarning(
            "Memory timer unlocked after repeated misses. Rescanning. HUD OCR stays the clock."
        );
        RestartSearch();
        LastRead = null;
    }

    private void ReplaceCandidates(IReadOnlyList<MemoryTimerCandidate> kept)
    {
        candidates.Clear();
        seen.Clear();
        for (int i = 0; i < kept.Count; i++)
        {
            candidates.Add(kept[i]);
            seen.Add(kept[i].Address);
        }
    }

    private void RestartSearch()
    {
        candidates.Clear();
        seen.Clear();
        locked = null;
        lockMisses = 0;
        pausedAtHud = -1;
        cursor = 0;
        totalBytes = 0;
        lastLoggedBand = -1;
        noisyRegions = 0;
        discoveryComplete = false;
        discoveryPaused = false;
        announcedPass = false;
        holdingLogged = false;
        cooldownUntil = DateTime.MinValue;
        mode = MemoryTimerKind.FloatSeconds;
    }

    private string ModeLabel => mode == MemoryTimerKind.Int32Seconds ? "int" : "float";

    private static TimeSpan NormalizeBudget(TimeSpan? scanBudget)
    {
        TimeSpan budget = scanBudget ?? DefaultScanBudget;
        if (budget < TimeSpan.FromMilliseconds(50))
        {
            return TimeSpan.FromMilliseconds(50);
        }

        if (budget > MaxScanBudget)
        {
            return MaxScanBudget;
        }

        return budget;
    }

    private static bool IsAlive(Process process)
    {
        if (process == null)
        {
            return false;
        }

        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static bool ShouldScan(Native.MemoryBasicInformation region)
    {
        if (region.State != Native.MemCommit || region.Type != Native.MemPrivate)
        {
            return false;
        }

        if ((region.Protect & Native.PageGuard) != 0 || (region.Protect & Native.PageNoAccess) != 0)
        {
            return false;
        }

        uint page = region.Protect & 0xFF;
        return page == Native.PageReadWrite
            || page == Native.PageWriteCopy
            || page == Native.PageExecuteReadWrite
            || page == Native.PageExecuteWriteCopy;
    }

    private static bool TryRead(IntPtr handle, MemoryTimerCandidate candidate, out int seconds)
    {
        seconds = 0;
        byte[] scratch = new byte[4];
        if (
            !Native.ReadProcessMemory(handle, (IntPtr)candidate.Address, scratch, 4, out int read)
            || read != 4
        )
        {
            return false;
        }

        return MemoryTimerSelection.TryDecode(scratch, candidate.Kind, out seconds);
    }

    private static class Native
    {
        public const int ProcessVmRead = 0x0010;
        public const int ProcessQueryInformation = 0x0400;
        public const uint MemCommit = 0x1000;
        public const uint MemPrivate = 0x20000;
        public const uint PageNoAccess = 0x01;
        public const uint PageReadWrite = 0x04;
        public const uint PageWriteCopy = 0x08;
        public const uint PageExecuteReadWrite = 0x40;
        public const uint PageExecuteWriteCopy = 0x80;
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
