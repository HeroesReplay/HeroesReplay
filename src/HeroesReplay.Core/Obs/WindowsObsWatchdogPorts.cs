using System;
using System.Diagnostics;
using System.Linq;
using HeroesReplay.Core.Obs.Inspection;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// The OBS watchdog's real ports (#398): the process table, a short read-only websocket session,
/// <c>CloseMainWindow</c>, and a kill. Its start is <see cref="ObsLauncher"/> (#409), the same
/// detached launch as spectate's. Tests must not use this type; they give
/// <see cref="ObsWatchdog"/> fakes.
/// </summary>
public static class WindowsObsWatchdogPorts
{
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
