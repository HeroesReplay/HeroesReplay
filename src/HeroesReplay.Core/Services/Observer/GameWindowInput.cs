using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PInvoke;
using static PInvoke.User32;

namespace HeroesReplay.Core.Services.Observer;

internal static class GameWindowInput
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfScancode = 0x0008;
    private const uint MapvkVkToVsc = 0;

    public static bool TryActivate(IntPtr handle, ILogger logger)
    {
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        ShowWindow(handle, WindowShowStyle.SW_RESTORE);
        uint thisThread = GetCurrentThreadId();
        uint targetThread = GetWindowThreadProcessId(handle, out _);
        IntPtr foreground = GetForegroundWindow();
        uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
        bool attachedTarget = false;
        bool attachedForeground = false;

        try
        {
            if (thisThread != targetThread)
            {
                attachedTarget = AttachThreadInput(thisThread, targetThread, true);
            }

            if (
                foregroundThread != 0
                && foregroundThread != thisThread
                && foregroundThread != targetThread
            )
            {
                attachedForeground = AttachThreadInput(thisThread, foregroundThread, true);
            }

            BringWindowToTop(handle);
            SetForegroundWindow(handle);
            SetActiveWindow(handle);
            NativeMethodsSetFocus(handle);

            if (GetForegroundWindow() != handle)
            {
                SendVirtualKey(VirtualKey.VK_MENU, down: true);
                SetForegroundWindow(handle);
                SendVirtualKey(VirtualKey.VK_MENU, down: false);
            }

            bool ok = GetForegroundWindow() == handle;
            if (!ok)
            {
                logger?.LogWarning(
                    "Could not make Heroes of the Storm the foreground window (hwnd {Handle}).",
                    handle
                );
            }

            return ok;
        }
        finally
        {
            if (attachedForeground)
            {
                AttachThreadInput(thisThread, foregroundThread, false);
            }

            if (attachedTarget)
            {
                AttachThreadInput(thisThread, targetThread, false);
            }
        }
    }

    public static void SendKeys(IntPtr handle, VirtualKey[] keys, ILogger logger)
    {
        if (keys == null || keys.Length == 0)
        {
            return;
        }

        TryActivate(handle, logger);

        int count = keys.Length * 2;
        var inputs = new INPUT[count];
        for (int i = 0; i < keys.Length; i++)
        {
            inputs[i] = Key(keys[i], up: false);
        }

        for (int i = 0; i < keys.Length; i++)
        {
            inputs[keys.Length + i] = Key(keys[keys.Length - 1 - i], up: true);
        }

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            logger?.LogWarning(
                "SendInput sent {Sent}/{Expected} events to hwnd {Handle}.",
                sent,
                inputs.Length,
                handle
            );
        }
    }

    public static IntPtr FindLargestVisibleWindow(int processId, int minWidth, int minHeight)
    {
        IntPtr best = IntPtr.Zero;
        int bestArea = 0;
        IntPtr titled = IntPtr.Zero;
        int titledArea = 0;

        EnumWindows(
            (h, l) =>
            {
                GetWindowThreadProcessId(h, out int windowPid);
                if (windowPid != processId || !IsWindowVisible(h))
                {
                    return true;
                }

                GetClientRect(h, out RECT client);
                int width = client.right - client.left;
                int height = client.bottom - client.top;
                int area = width * height;
                if (area <= 0)
                {
                    return true;
                }

                if (area > bestArea)
                {
                    bestArea = area;
                    best = h;
                }

                if (
                    area >= minWidth * minHeight
                    && GetWindowText(h)
                        .IndexOf("Heroes of the Storm", StringComparison.OrdinalIgnoreCase) >= 0
                    && area > titledArea
                )
                {
                    titledArea = area;
                    titled = h;
                }

                return true;
            },
            IntPtr.Zero
        );

        if (titled != IntPtr.Zero)
        {
            return titled;
        }

        return best;
    }

    private static INPUT Key(VirtualKey key, bool up)
    {
        ushort vk = (ushort)key;
        ushort scan = (ushort)MapVirtualKey(vk, MapvkVkToVsc);
        uint flags = KeyeventfScancode;
        if (up)
        {
            flags |= KeyeventfKeyup;
        }

        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
    }

    private static void SendVirtualKey(VirtualKey key, bool down)
    {
        var input = new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)key,
                    wScan = 0,
                    dwFlags = down ? 0 : KeyeventfKeyup,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "SetFocus")]
    private static extern IntPtr NativeMethodsSetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
