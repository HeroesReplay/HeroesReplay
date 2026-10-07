using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HeroesReplay.Core.Spectating.Clock;

namespace HeroesReplay.Core.Spectating.Screens;

public enum ClientScreen
{
    Unknown,
    Menu,
    Loading,
    Match,
}

public readonly record struct LoadingScreenSample(ClientScreen Screen, bool MenuSeen, string Reason)
{
    /// <summary>
    /// True on a map loading screen, false on a menu or in a match, null when memory cannot
    /// tell. The client's boot splash is a loading screen too, so a loading screen counts only
    /// after this process has shown a menu. Null means: read the screen instead.
    /// </summary>
    public bool? MapLoading =>
        !MenuSeen ? null
        : Screen == ClientScreen.Loading ? true
        : Screen is ClientScreen.Menu or ClientScreen.Match ? false
        : null;

    /// <summary>
    /// True on a menu, false on a loading screen or in a match once this process has shown a
    /// menu, null when memory cannot tell.
    /// </summary>
    public bool? OnMenu =>
        Screen == ClientScreen.Menu ? true
        : MenuSeen && Screen is ClientScreen.Loading or ClientScreen.Match ? false
        : null;

    /// <summary>
    /// The client is in a match: no screen object, after this process has shown a menu. A match
    /// is a replay already on screen, so the launch does not wait for a menu that cannot come
    /// (#249). Before the first menu, memory does not count it; the match clock still does.
    /// </summary>
    public bool InMatch => MenuSeen && Screen == ClientScreen.Match;
}

/// <summary>
/// Read-only loading-screen state from client memory, so "WELCOME TO" does not need OCR.
/// [global + 0x218] is the screen object: null in a match, otherwise bit 0 of byte 72 is set
/// on a loading screen (boot splash or map) and clear on a menu. Measured on 2.57.0.98304:
/// boot splash 1, home 0, map loading 1, match null. The global is found per build by
/// <see cref="LoadingScreenPattern"/>; the two offsets are fixed.
/// </summary>
public sealed class LoadingScreenMemory : IDisposable
{
    public const long ScreenOffset = 0x218;
    public const long FlagsOffset = 72;
    private static readonly TimeSpan RediscoverAfter = TimeSpan.FromSeconds(10);

    private IntPtr handle;
    private int attachedPid;
    private int pid;
    private long startedAt;
    private long moduleBase;
    private long moduleSize;
    private bool discovered;
    private long globalRva;
    private bool menuSeen;
    private DateTimeOffset rediscoverAt;
    private string reason = "no-process";

    internal Func<DateTimeOffset> UtcNow { get; set; } = () => DateTimeOffset.UtcNow;

    internal long GlobalRva => globalRva;

    public LoadingScreenSample Read(Process process)
    {
        if (!TryAttach(process, out StableClockModule module))
        {
            return new LoadingScreenSample(ClientScreen.Unknown, false, reason);
        }

        return Read(module, ReadProcess);
    }

    internal LoadingScreenSample Read(StableClockModule module, Func<long, byte[], bool> read)
    {
        if (module.ProcessId <= 0 || module.BaseAddress <= 0 || module.Size <= 0 || read == null)
        {
            return Sample(ClientScreen.Unknown, "no-module");
        }

        UseModule(module);
        if (!discovered || (globalRva == 0 && UtcNow() >= rediscoverAt))
        {
            Discover(read);
        }

        if (globalRva == 0)
        {
            return Sample(ClientScreen.Unknown, reason);
        }

        if (!TryReadPointer(read, moduleBase + globalRva, out long state))
        {
            return Sample(ClientScreen.Unknown, "read-failed");
        }

        if (state == 0)
        {
            return Sample(ClientScreen.Unknown, "no-state");
        }

        if (!TryReadPointer(read, state + ScreenOffset, out long screen))
        {
            return Sample(ClientScreen.Unknown, "read-failed");
        }

        if (screen == 0)
        {
            return Sample(ClientScreen.Match, "match");
        }

        byte[] flags = new byte[1];
        if (!TryRead(read, screen + FlagsOffset, flags))
        {
            return Sample(ClientScreen.Unknown, "read-failed");
        }

        if ((flags[0] & 1) == 0)
        {
            menuSeen = true;
            return Sample(ClientScreen.Menu, "menu");
        }

        return Sample(ClientScreen.Loading, "loading");
    }

    public void Dispose()
    {
        ReleaseHandle();
    }

    private LoadingScreenSample Sample(ClientScreen screen, string why) =>
        new(screen, menuSeen, why);

    private void UseModule(StableClockModule module)
    {
        if (
            pid == module.ProcessId
            && startedAt == module.StartedAt
            && moduleBase == module.BaseAddress
            && moduleSize == module.Size
        )
        {
            return;
        }

        // A relaunched client starts over: its global and its first menu are its own.
        pid = module.ProcessId;
        startedAt = module.StartedAt;
        moduleBase = module.BaseAddress;
        moduleSize = module.Size;
        discovered = false;
        globalRva = 0;
        menuSeen = false;
        rediscoverAt = default;
    }

    private void Discover(Func<long, byte[], bool> read)
    {
        discovered = true;
        rediscoverAt = UtcNow() + RediscoverAfter;
        byte[] headers = new byte[0x1000];
        if (
            !TryRead(read, moduleBase, headers)
            || !MatchClockPattern.TryExecutableSections(headers, out var sections)
        )
        {
            reason = "read-failed";
            return;
        }

        var found = new List<long>();
        foreach (MatchClockPattern.Section section in sections)
        {
            if (
                section.VirtualSize <= 0
                || section.VirtualAddress < 0
                || section.VirtualAddress + section.VirtualSize > moduleSize
            )
            {
                continue;
            }

            Collect(read, section.VirtualAddress, section.VirtualSize, found);
        }

        // Code that is still being unpacked has no sites yet; the next attempt reads it again.
        if (
            !LoadingScreenPattern.TryAgree(found, out long rva, out _)
            || rva <= 0
            || rva > moduleSize - 8
        )
        {
            reason = found.Count == 0 ? "unsupported-build" : "pattern-disagreed";
            return;
        }

        globalRva = rva;
        reason = "pattern";
    }

    private void Collect(Func<long, byte[], bool> read, long rva, int size, List<long> found)
    {
        if (size > 64 * 1024 * 1024)
        {
            return;
        }

        const int chunk = 1 << 20;
        for (int offset = 0; offset < size; offset += chunk)
        {
            int count = Math.Min(size - offset, chunk + LoadingScreenPattern.Width);
            byte[] slice = new byte[count];
            if (TryRead(read, moduleBase + rva + offset, slice))
            {
                found.AddRange(LoadingScreenPattern.Find(slice, rva + offset));
            }
        }
    }

    /// <summary>
    /// A pointer is zero or a user-mode address. Anything else is not this structure.
    /// </summary>
    private static bool TryReadPointer(Func<long, byte[], bool> read, long address, out long value)
    {
        value = 0;
        byte[] buffer = new byte[8];
        if (!TryRead(read, address, buffer))
        {
            return false;
        }

        value = BitConverter.ToInt64(buffer, 0);
        return value == 0 || (value >= 0x10000 && value <= 0x7FFF_FFFF_FFFF);
    }

    private static bool TryRead(Func<long, byte[], bool> read, long address, byte[] buffer)
    {
        return address > 0 && read(address, buffer);
    }

    private bool TryAttach(Process process, out StableClockModule module)
    {
        module = default;
        try
        {
            if (process == null || process.HasExited)
            {
                reason = "no-process";
                return false;
            }

            if (handle == IntPtr.Zero || attachedPid != process.Id)
            {
                ReleaseHandle();
                handle = Native.OpenProcess(
                    Native.ProcessQueryInformation | Native.ProcessVmRead,
                    false,
                    process.Id
                );
                if (handle == IntPtr.Zero)
                {
                    reason = "open-failed";
                    return false;
                }

                attachedPid = process.Id;
            }

            ProcessModule main = process.MainModule;
            if (main == null || main.BaseAddress == IntPtr.Zero || main.ModuleMemorySize <= 0)
            {
                reason = "no-module";
                return false;
            }

            module = new StableClockModule(
                process.Id,
                main.BaseAddress.ToInt64(),
                main.ModuleMemorySize,
                main.FileVersionInfo.FileVersion,
                StableClockModule.StartTicks(process)
            );
            return true;
        }
        catch
        {
            reason = "no-process";
            return false;
        }
    }

    private bool ReadProcess(long address, byte[] buffer)
    {
        return handle != IntPtr.Zero
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
