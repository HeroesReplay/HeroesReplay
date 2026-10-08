using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using static PInvoke.User32;
using RECT = PInvoke.RECT;

namespace HeroesReplay.Core.GameClient;

/// <summary>A child control of a client window: its class, dialog control id and text.</summary>
public readonly record struct ClientWindowChild(string ClassName, int ControlId, string Text);

/// <summary>
/// One top-level window of a client process as Win32 reports it: class, title, visibility,
/// client size and its child controls. Nothing here reads pixels or the process's memory.
/// </summary>
public sealed record ClientWindow(
    IntPtr Handle,
    int ProcessId,
    string ClassName,
    string Title,
    bool Visible,
    int Width,
    int Height,
    IReadOnlyList<ClientWindowChild> Children
);

/// <summary>
/// The top-level windows of one process. The Win32 implementation is
/// <see cref="Win32ClientWindows"/>; tests serve a fixed list.
/// </summary>
public interface IClientWindows
{
    IReadOnlyList<ClientWindow> TopLevel(int processId);
}

/// <summary>
/// <c>EnumWindows</c>, <c>GetClassName</c>, <c>GetWindowText</c> and <c>EnumChildWindows</c>
/// over one process's top-level windows. It never opens the process, sends a message, or
/// touches input: every call only reads what the window manager already keeps (#292).
/// </summary>
internal sealed class Win32ClientWindows : IClientWindows
{
    public const int MaxChildren = 32;
    private const int ClassNameLength = 256;

    public IReadOnlyList<ClientWindow> TopLevel(int processId)
    {
        var windows = new List<ClientWindow>();
        if (processId <= 0)
        {
            return windows;
        }

        EnumWindows(
            (handle, _) =>
            {
                GetWindowThreadProcessId(handle, out int windowPid);
                if (windowPid == processId)
                {
                    windows.Add(Describe(handle, windowPid));
                }

                return true;
            },
            IntPtr.Zero
        );

        return windows;
    }

    private static ClientWindow Describe(IntPtr handle, int processId)
    {
        GetClientRect(handle, out RECT client);
        var children = new List<ClientWindowChild>();
        EnumChildWindows(
            handle,
            (child, _) =>
            {
                children.Add(
                    new ClientWindowChild(ClassOf(child), ControlIdOf(child), TextOf(child))
                );
                return children.Count < MaxChildren;
            },
            IntPtr.Zero
        );

        return new ClientWindow(
            handle,
            processId,
            ClassOf(handle),
            TextOf(handle),
            IsWindowVisible(handle),
            Math.Max(0, client.right - client.left),
            Math.Max(0, client.bottom - client.top),
            children
        );
    }

    private static string ClassOf(IntPtr handle)
    {
        try
        {
            return GetClassName(handle, ClassNameLength);
        }
        catch (Win32Exception)
        {
            return string.Empty;
        }
    }

    // For a window of another process, GetWindowText returns the text the window manager keeps
    // and sends no message, so a hung client cannot block the caller.
    private static string TextOf(IntPtr handle)
    {
        try
        {
            return GetWindowText(handle) ?? string.Empty;
        }
        catch (Win32Exception)
        {
            return string.Empty;
        }
    }

    private static int ControlIdOf(IntPtr handle)
    {
        try
        {
            return GetDlgCtrlID(handle);
        }
        catch (Win32Exception)
        {
            return 0;
        }
    }

    private delegate bool ChildEnumProc(IntPtr handle, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(
        IntPtr hWndParent,
        ChildEnumProc lpEnumFunc,
        IntPtr lParam
    );

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr hWnd);
}
