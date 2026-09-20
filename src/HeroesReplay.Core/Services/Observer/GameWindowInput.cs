using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PInvoke;
using static PInvoke.User32;

namespace HeroesReplay.Core.Services.Observer;

internal static class GameWindowInput
{
    private const int WmKeydown = 0x0100;
    private const int WmKeyup = 0x0101;
    private const uint MapvkVkToVsc = 0;

    public static void SendKeys(IntPtr handle, VirtualKey[] keys, ILogger logger)
    {
        if (handle == IntPtr.Zero || keys == null || keys.Length == 0)
        {
            return;
        }

        var targets = new List<IntPtr> { handle };
        EnumChildWindows(
            handle,
            (child, l) =>
            {
                if (IsWindowVisible(child))
                {
                    targets.Add(child);
                }

                return true;
            },
            IntPtr.Zero
        );

        foreach (VirtualKey key in keys)
        {
            PostKey(targets, key, down: true);
        }

        for (int i = keys.Length - 1; i >= 0; i--)
        {
            PostKey(targets, keys[i], down: false);
        }

        logger?.LogDebug(
            "Posted {Count} key(s) to {Windows} HWND(s) of the game process.",
            keys.Length,
            targets.Count
        );
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

        return titled != IntPtr.Zero ? titled : best;
    }

    private static void PostKey(List<IntPtr> windows, VirtualKey key, bool down)
    {
        ushort vk = (ushort)key;
        uint scan = MapVirtualKey(vk, MapvkVkToVsc);
        uint lParam = 1u | (scan << 16);
        if (!down)
        {
            lParam |= (1u << 30) | (1u << 31);
        }

        int message = down ? WmKeydown : WmKeyup;
        IntPtr wParam = (IntPtr)vk;
        IntPtr lp = unchecked((IntPtr)(int)lParam);
        foreach (IntPtr hwnd in windows)
        {
            PostMessage(hwnd, message, wParam, lp);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(
        IntPtr hWndParent,
        WNDENUMPROC lpEnumFunc,
        IntPtr lParam
    );

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
