using System;
using System.Collections.Generic;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>What <c>services shutdown</c> asks Windows to do once everything is closed.</summary>
public enum MachinePowerAction
{
    /// <summary>Close everything and leave Windows running.</summary>
    None,
    Restart,
    PowerOff,
}

/// <summary>How OBS ended during <c>services shutdown</c>.</summary>
public enum ObsCloseOutcome
{
    NotRunning,

    /// <summary>OBS closed after its stream and recording stopped and its window was asked to close.</summary>
    Closed,

    /// <summary>OBS was still running after the close wait and was killed.</summary>
    Killed,

    /// <summary>An obs64 is still running after the kill.</summary>
    StillRunning,

    /// <summary>OBS was left running: the stack did not stop, and <c>--force</c> was not given.</summary>
    Skipped,
}

/// <summary>The websocket part of closing OBS: stop the stream and the recording first.</summary>
public sealed record ObsOutputsStop(bool Connected, bool StreamStopped, bool RecordingStopped, string Detail)
{
    public static ObsOutputsStop Unreachable(string detail) => new(false, false, false, detail);
}

/// <summary>
/// <c>services shutdown</c>: take the whole machine's automation down cleanly before a restart.
/// <c>services stop</c> (the supervisor, every role, the game, idle switchers, a left recording)
/// first; then OBS, which <c>services stop</c> never touches: its stream and recording are
/// stopped over the websocket so the recording is finalized and Twitch sees the stream end, its
/// window is asked to close, and an obs64 still there after <see cref="ObsCloseWait"/> is
/// killed. Only then, and only when everything closed (or <c>--force</c>), is Windows asked to
/// restart or power off. Tests replace each step.
/// </summary>
public sealed class ServiceMachineShutdown
{
    public static readonly TimeSpan DefaultObsCloseWait = TimeSpan.FromSeconds(20);

    /// <summary>Runs <c>services stop</c>.</summary>
    public Func<ServiceStopResult> StopStack { get; init; }

    /// <summary>The running obs64 pids.</summary>
    public Func<IReadOnlyList<int>> FindObs { get; init; }

    /// <summary>Stops OBS's stream and recording over its websocket and waits for both to end.</summary>
    public Func<ObsOutputsStop> StopObsOutputs { get; init; }

    /// <summary>Asks one obs64 to close (<c>CloseMainWindow</c>).</summary>
    public Action<int> CloseObs { get; init; }

    /// <summary>Waits for one pid to exit; true when it is gone.</summary>
    public Func<int, TimeSpan, bool> WaitForExit { get; init; }
    public Action<int> KillObs { get; init; }
    public TimeSpan ObsCloseWait { get; init; } = DefaultObsCloseWait;

    /// <summary>Asks Windows to restart or power off after the delay. Returns shutdown.exe's exit code.</summary>
    public Func<MachinePowerAction, TimeSpan, int> RequestPower { get; init; }

    public Action<string> Write { get; init; } = Console.WriteLine;

    public ServiceMachineShutdownResult Run(MachinePowerAction action, TimeSpan delay, bool force)
    {
        ArgumentNullException.ThrowIfNull(StopStack);
        ServiceStopResult stack = StopStack();
        bool stackStopped = StackStopped(stack);
        if (!stackStopped)
        {
            Write(
                force
                    ? "The stack did not stop cleanly. --force: OBS is closed anyway."
                    : "The stack did not stop cleanly (see above), so OBS is left running and Windows is not asked to restart. Fix what is still running, or pass --force."
            );
        }

        ObsCloseOutcome obs =
            stackStopped || force ? CloseObsProcesses() : ObsCloseOutcome.Skipped;
        bool obsClosed = obs is ObsCloseOutcome.NotRunning or ObsCloseOutcome.Closed or ObsCloseOutcome.Killed;
        bool clean = stackStopped && obsClosed;

        int? powerExit = null;
        if (action != MachinePowerAction.None)
        {
            if (clean || force)
            {
                string what = action == MachinePowerAction.Restart ? "restart" : "power off";
                powerExit = RequestPower?.Invoke(action, delay) ?? 1;
                Write(
                    powerExit == 0
                        ? $"Windows will {what} in {delay.TotalSeconds:0} s. Cancel with `shutdown /a`. The HeroesReplay-live task starts the stack at the next logon (`heroesreplay check autostart`)."
                        : $"shutdown.exe exited {powerExit}: Windows was not asked to {what}."
                );
            }
            else
            {
                Write("Windows was not asked to restart or power off, because something is still running.");
            }
        }

        return new ServiceMachineShutdownResult(stack, stackStopped, obs, action, powerExit);
    }

    /// <summary>
    /// The parts of <c>services stop</c> a machine shutdown needs: every role, the supervisor
    /// and the game are gone. The stream and the recording are OBS's, which this command closes
    /// next, so they do not count here.
    /// </summary>
    public static bool StackStopped(ServiceStopResult stop) =>
        stop != null
        && stop.RolesExited
        && stop.GameClosed != false
        && stop.Supervisor?.Exited != false;

    private ObsCloseOutcome CloseObsProcesses()
    {
        IReadOnlyList<int> pids = FindObs?.Invoke() ?? Array.Empty<int>();
        if (pids.Count == 0)
        {
            Write("OBS: not running.");
            return ObsCloseOutcome.NotRunning;
        }

        ObsOutputsStop outputs = StopObsOutputs?.Invoke();
        if (outputs != null)
        {
            Write("OBS: " + outputs.Detail);
        }

        bool killed = false;
        var left = new List<int>();
        foreach (int pid in pids)
        {
            try
            {
                CloseObs?.Invoke(pid);
            }
            catch (Exception e)
            {
                Write($"OBS pid {pid}: close failed ({e.Message}).");
            }

            if (WaitForExit?.Invoke(pid, ObsCloseWait) == true)
            {
                Write($"OBS pid {pid}: closed.");
                continue;
            }

            Write($"OBS pid {pid}: still running {ObsCloseWait.TotalSeconds:0} s after the close request. Killing it.");
            try
            {
                KillObs?.Invoke(pid);
            }
            catch (Exception e)
            {
                Write($"OBS pid {pid}: kill failed ({e.Message}).");
            }

            if (WaitForExit?.Invoke(pid, TimeSpan.FromSeconds(5)) == true)
            {
                killed = true;
            }
            else
            {
                left.Add(pid);
            }
        }

        if (left.Count > 0)
        {
            Write($"OBS: still running (pid {string.Join(", ", left)}).");
            return ObsCloseOutcome.StillRunning;
        }

        return killed ? ObsCloseOutcome.Killed : ObsCloseOutcome.Closed;
    }
}

public sealed record ServiceMachineShutdownResult(
    ServiceStopResult Stack,
    bool StackStopped,
    ObsCloseOutcome Obs,
    MachinePowerAction Action,
    int? PowerExitCode
)
{
    public bool Succeeded =>
        StackStopped
        && Obs is ObsCloseOutcome.NotRunning or ObsCloseOutcome.Closed or ObsCloseOutcome.Killed
        && (Action == MachinePowerAction.None || PowerExitCode == 0);

    public int ExitCode => Succeeded ? 0 : 1;
}
