using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace HeroesReplay.Core.GameClient;

/// <summary>
/// What the client's windows say about "Preparing game data" on one process: shown, not shown,
/// or null when the windows could not be read. <see cref="Reason"/> describes the read for logs.
/// </summary>
public readonly record struct GameDataWindowSample(bool? Shown, string Reason);

/// <summary>
/// "Preparing game data" is not client UI: it is a native Win32 dialog the client exe keeps in its
/// resources (#292). The template is the same in 2.55.17.98025, 2.57.0.98285, 2.57.0.98304 and
/// 2.57.0.98348: a <c>DLGTEMPLATEEX</c> titled "Progress", 269x88 dialog units (404x143 pixels,
/// the window OCR read the text from), with a static message (id 30101, set at run time to
/// <c>DownloadProgressMessage</c>, "Preparing game data"), a <c>msctls_progress32</c> bar (30102),
/// a status static (30103, "Calculating...") and Cancel (<c>IDCANCEL</c>). It has no class of its
/// own, so it is the standard dialog class <c>#32770</c>. A visible top-level <c>#32770</c> window
/// of the client process with a <c>msctls_progress32</c> child is that dialog. The read uses
/// class names only, so it does not depend on the client language and needs no OCR.
/// </summary>
public static class GameDataProgressWindow
{
    public const string DialogClass = "#32770";
    public const string ProgressClass = "msctls_progress32";
    public const int MessageControlId = 30101;

    public static bool IsProgressDialog(ClientWindow window) =>
        window != null
        && window.Visible
        && string.Equals(window.ClassName, DialogClass, StringComparison.Ordinal)
        && (window.Children ?? Array.Empty<ClientWindowChild>()).Any(child =>
            string.Equals(child.ClassName, ProgressClass, StringComparison.OrdinalIgnoreCase)
        );

    public static ClientWindow Find(IEnumerable<ClientWindow> windows) =>
        windows?.FirstOrDefault(IsProgressDialog);

    /// <summary>
    /// The verdict for one client process: true while its progress dialog shows, false while the
    /// process has no such window, null with no process or when the windows cannot be read.
    /// </summary>
    public static GameDataWindowSample Read(IClientWindows source, int? processId)
    {
        if (source == null || processId is not int pid || pid <= 0)
        {
            return new GameDataWindowSample(null, "no client process");
        }

        IReadOnlyList<ClientWindow> windows;
        try
        {
            windows = source.TopLevel(pid) ?? Array.Empty<ClientWindow>();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return new GameDataWindowSample(null, "windows not read: " + e.Message);
        }

        ClientWindow dialog = Find(windows);
        return dialog == null
            ? new GameDataWindowSample(
                false,
                $"no progress dialog, {windows.Count(w => w.Visible)} visible window(s) of pid {pid}"
            )
            : new GameDataWindowSample(true, Describe(dialog));
    }

    /// <summary>The dialog for a log line: class, title, size, pid and its message text.</summary>
    public static string Describe(ClientWindow window)
    {
        if (window == null)
        {
            return "no window";
        }

        string message = (window.Children ?? Array.Empty<ClientWindowChild>())
            .FirstOrDefault(child => child.ControlId == MessageControlId)
            .Text;
        string text = string.IsNullOrWhiteSpace(message) ? "" : $", message \"{message.Trim()}\"";
        return $"progress dialog {window.ClassName} \"{window.Title}\" {window.Width}x{window.Height}, pid {window.ProcessId}{text}";
    }
}
