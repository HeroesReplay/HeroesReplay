using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// The OBS watchdog's real ports (#398): the process table, a short read-only websocket session,
/// <c>CloseMainWindow</c>, a kill, and a detached start. Tests must not use this type; they give
/// <see cref="ObsWatchdog"/> fakes.
/// </summary>
public static class WindowsObsWatchdogPorts
{
    private static readonly TimeSpan LauncherWait = TimeSpan.FromSeconds(15);

    /// <summary>The oldest obs64 process, or null.</summary>
    public static ObsProcessInfo FindObs()
    {
        Process[] processes = Process.GetProcessesByName(ObsLaunchDecision.ProcessName);
        try
        {
            return processes
                .Select(process => new ObsProcessInfo(process.Id, StartTime(process)))
                .OrderBy(process => process.StartedAt ?? DateTimeOffset.MaxValue)
                .FirstOrDefault();
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// Identify, read <c>GetStreamStatus</c>, disconnect: the read-only session of the MCP tools,
    /// never the spectator's connection. A rejected password is an answer: OBS is alive.
    /// </summary>
    public static ObsWatchdogProbe Probe(string endpoint, string password)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return ObsWatchdogProbe.Unconfigured("OBS:WebSocketEndpoint is empty.");
        }

        try
        {
            using IObsReadSession session = new ObsWebsocketReadSessionFactory().Open(
                endpoint,
                password
            );
            return ObsWatchdogProbe.Answered(session.Get("GetStreamStatus"));
        }
        catch (ObsUnavailableException e)
            when (e.Code == ObsUnavailableException.AuthenticationFailed)
        {
            return ObsWatchdogProbe.Answered(null, e.Message);
        }
        catch (ObsUnavailableException e)
        {
            return ObsWatchdogProbe.NoAnswer(e.Message);
        }
        catch (ObsRequestException e)
            when (e.Status == 0
                || e.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            )
        {
            // obs-websocket-dotnet reports a request that got no answer as "Request timed out".
            return ObsWatchdogProbe.NoAnswer(e.Message);
        }
        catch (ObsRequestException e)
        {
            return ObsWatchdogProbe.Answered(null, e.Message);
        }
    }

    /// <summary>Null when this process runs in an interactive session; else why OBS would not be visible.</summary>
    public static string CannotControl()
    {
        using Process self = Process.GetCurrentProcess();
        return self.SessionId == 0
            ? "the supervisor runs in session 0 (a service or an SSH logon), where an OBS it started would not be visible. Run it from the desktop (the HeroesReplay-live task)."
            : null;
    }

    public static void Close(int pid)
    {
        using Process process = Process.GetProcessById(pid);
        process.CloseMainWindow();
    }

    public static bool WaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.WaitForExit(timeout);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    public static void Kill(int pid)
    {
        using Process process = Process.GetProcessById(pid);
        process.Kill(entireProcessTree: true);
    }

    /// <summary>
    /// Starts OBS through <c>cmd /c start</c>, so it is not a child of the supervisor: a stop that
    /// kills the supervisor's process tree never takes OBS with it. Inside
    /// <see cref="ObsLaunchGate"/>, and only when no obs64 runs. Null on success, else why not.
    /// </summary>
    public static string Start(string path, string arguments)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return $"OBS was not found at `{path}`. Set OBS:ExecutablePath.";
        }

        using ObsLaunchGate gate = ObsLaunchGate.Enter(ObsLaunchGate.DefaultWait);
        if (!gate.Held)
        {
            return "another OBS launch held the launch gate.";
        }

        if (NamedProcess.IsRunning(ObsLaunchDecision.ProcessName))
        {
            return null;
        }

        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            Arguments = $"/d /c start \"\" /D \"{directory}\" \"{path}\" {arguments}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory,
        };
        using Process launcher = Process.Start(start);
        if (launcher == null)
        {
            return "cmd.exe did not start.";
        }

        if (!launcher.WaitForExit(LauncherWait))
        {
            return $"cmd.exe did not return within {LauncherWait.TotalSeconds:0}s.";
        }

        return launcher.ExitCode == 0 ? null : $"cmd.exe exited {launcher.ExitCode}.";
    }

    /// <summary>Deletes stale <c>run_*</c> sentinels while no OBS runs (<see cref="ObsCrashSentinel"/>).</summary>
    public static void RemoveStaleSentinels(ILogger logger) =>
        ObsCrashSentinel.ForThisUser(logger).RemoveStale();

    private static DateTimeOffset? StartTime(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime).ToUniversalTime();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
