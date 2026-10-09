using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using static PInvoke.User32;

namespace HeroesReplay.Core.Obs;

/// <summary>One top-level window as Win32 reports it: its process, title, and whether it shows.</summary>
internal sealed record ObsTopWindow(
    IntPtr Handle,
    int ProcessId,
    string Title,
    bool Visible,
    bool Hung
);

/// <summary>
/// The top-level windows on the desktop, and a close request to one. The Win32 implementation is
/// <see cref="Win32ObsWindows"/>; tests serve a fixed list and record the closes.
/// </summary>
internal interface IObsWindows
{
    IReadOnlyList<ObsTopWindow> TopLevel();

    /// <summary>Posts <c>WM_CLOSE</c>. True when the message was queued.</summary>
    bool Close(IntPtr handle);
}

/// <summary>
/// OBS's "Failed to connect" dialog (#407). Every StartStream that cannot reach the ingest opens
/// one, modal, and they stack: on ASA-SERVER on 2026-10-09 four were open when OBS stopped
/// answering. After a failed start, while OBS answers its websocket, each visible top-level
/// window of the OBS process whose title is exactly <c>OBS:ConnectFailDialogTitle</c> gets
/// <c>WM_CLOSE</c>, which OBS handles like the dialog's OK. No other window, of OBS or of any other
/// process, is touched, and a window Windows reports as not responding is left alone.
/// </summary>
internal static class ObsConnectFailDialog
{
    /// <summary>OBS's <c>Output.ConnectFail.Title</c> in en-US.</summary>
    public const string DefaultTitle = "Failed to connect";

    /// <summary>The number of dialogs a close was posted to. Never throws.</summary>
    public static int Close(IObsWindows windows, int? obsPid, string title, ILogger logger)
    {
        if (windows == null || obsPid is not int pid || pid <= 0 || string.IsNullOrEmpty(title))
        {
            return 0;
        }

        IReadOnlyList<ObsTopWindow> found;
        try
        {
            found = windows.TopLevel();
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not list the top-level windows to find OBS's dialogs.");
            return 0;
        }

        int closed = 0;
        foreach (ObsTopWindow window in found ?? [])
        {
            if (
                window == null
                || window.ProcessId != pid
                || !window.Visible
                || !string.Equals(window.Title, title, StringComparison.Ordinal)
            )
            {
                continue;
            }

            if (window.Hung)
            {
                logger.LogWarning(
                    "OBS's \"{Title}\" dialog (window 0x{Handle:X}, OBS pid {Pid}) is not responding, so it was left open.",
                    title,
                    window.Handle.ToInt64(),
                    pid
                );
                continue;
            }

            bool posted;
            try
            {
                posted = windows.Close(window.Handle);
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "Could not post WM_CLOSE to OBS's dialog.");
                posted = false;
            }

            if (!posted)
            {
                continue;
            }

            closed++;
            logger.LogInformation(
                "Closed OBS's \"{Title}\" dialog (window 0x{Handle:X}, OBS pid {Pid}) after a stream start that did not connect.",
                title,
                window.Handle.ToInt64(),
                pid
            );
        }

        return closed;
    }
}

/// <summary>
/// <c>EnumWindows</c>, <c>GetWindowThreadProcessId</c>, <c>IsWindowVisible</c>,
/// <c>GetWindowText</c> and <c>IsHungAppWindow</c>, and <c>PostMessage(WM_CLOSE)</c> for the close.
/// Listing sends no message, so a hung OBS cannot block it, and a posted close does not wait.
/// </summary>
internal sealed class Win32ObsWindows : IObsWindows
{
    public IReadOnlyList<ObsTopWindow> TopLevel()
    {
        var windows = new List<ObsTopWindow>();
        EnumWindows(
            (handle, _) =>
            {
                GetWindowThreadProcessId(handle, out int pid);
                windows.Add(
                    new ObsTopWindow(
                        handle,
                        pid,
                        TextOf(handle),
                        IsWindowVisible(handle),
                        IsHungAppWindow(handle)
                    )
                );
                return true;
            },
            IntPtr.Zero
        );
        return windows;
    }

    public bool Close(IntPtr handle) =>
        PostMessage(handle, WindowMessage.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    // For a window of another process, GetWindowText returns the text the window manager keeps
    // and sends no message.
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
}
