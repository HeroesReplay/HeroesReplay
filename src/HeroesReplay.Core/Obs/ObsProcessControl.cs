using System;
using System.ComponentModel;
using System.Diagnostics;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Obs;

internal interface IObsProcess
{
    bool IsRunning();

    /// <summary>The oldest running obs64's pid, or null: whose windows the dialog close may touch.</summary>
    int? ProcessId();
    bool ExecutableExists(string path);
    bool IsOwned { get; }
    ObsLaunchDecision Start(ObsLaunchDecision decision);
    void CloseOwned();
}

/// <summary>
/// Spectate's OBS process (#409). It starts OBS only through <see cref="ObsLauncher"/>, detached,
/// so OBS is never spectate's child, and owns the obs64 that launch found by its pid and start
/// time: <see cref="CloseOwned"/> (<c>OBS:CloseOwnedOnStop</c>) closes only that process, never a
/// process that reused the pid. Tests give it a launcher on a fake starter and table, a fake
/// table, and a fake close; the coordinator's own tests pass a fake <see cref="IObsProcess"/>.
/// </summary>
internal sealed class WindowsObsProcess : IObsProcess
{
    private readonly ObsLauncher launcher;
    private readonly IProcessTable table;
    private readonly Func<ProcessTableEntry, bool> closeMainWindow;
    private readonly Func<bool> obsRunning;
    private ProcessTableEntry owned;

    public WindowsObsProcess(
        ObsLauncher launcher,
        IProcessTable table = null,
        Func<ProcessTableEntry, bool> closeMainWindow = null,
        Func<bool> obsRunning = null
    )
    {
        this.launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        this.table = table ?? WindowsProcessTable.Instance;
        this.closeMainWindow = closeMainWindow ?? CloseMainWindow;
        this.obsRunning =
            obsRunning ?? (() => NamedProcess.IsRunning(ObsLaunchDecision.ProcessName));
    }

    /// <summary>The obs64 this process started still runs: same pid, same start time.</summary>
    public bool IsOwned => Current() != null;

    public bool IsRunning() => obsRunning();

    public int? ProcessId() => WindowsObsWatchdogPorts.FindObs()?.Pid;

    public bool ExecutableExists(string path) =>
        !string.IsNullOrWhiteSpace(path) && launcher.ExecutableExists(path);

    public ObsLaunchDecision Start(ObsLaunchDecision decision)
    {
        if (decision == null || decision.Kind != ObsLaunchKind.Launch)
        {
            return decision
                ?? new ObsLaunchDecision
                {
                    Kind = ObsLaunchKind.Skipped,
                    Detail = "OBS launch was not requested.",
                };
        }

        ObsLaunch launch = launcher.Launch(decision.ExecutablePath, decision.Arguments);
        switch (launch.Outcome)
        {
            case ObsLaunchOutcome.Started:
                owned = launch.Obs;
                return decision with { Started = true, Detail = launch.Detail };
            case ObsLaunchOutcome.AlreadyRunning:
                return decision with
                {
                    Kind = ObsLaunchKind.AlreadyRunning,
                    Started = false,
                    Detail =
                        "OBS started meanwhile (the supervisor's OBS watchdog). It is not owned.",
                };
            default:
                return decision with { Started = false, Detail = launch.Detail };
        }
    }

    public void CloseOwned()
    {
        ProcessTableEntry process = Current();
        owned = null;
        if (process == null)
        {
            // Gone, or its pid now belongs to another process. Do not close obs64 by name.
            return;
        }

        try
        {
            closeMainWindow(process);
        }
        catch (Exception)
        {
            // Close failed. Do not kill an OBS process to finish shutdown.
        }
    }

    /// <summary>The owned obs64 as the table shows it now, or null when it is gone or replaced.</summary>
    private ProcessTableEntry Current()
    {
        ProcessTableEntry started = owned;
        if (started == null)
        {
            return null;
        }

        ProcessTableEntry now;
        try
        {
            now = table.Find(started.Pid);
        }
        catch (Exception)
        {
            return null;
        }

        return now != null && now.StartTime != null && now.StartTime == started.StartTime
            ? now
            : null;
    }

    private static bool CloseMainWindow(ProcessTableEntry obs)
    {
        try
        {
            using Process process = Process.GetProcessById(obs.Pid);
            if (
                obs.StartTime is not DateTimeOffset started
                || (new DateTimeOffset(process.StartTime) - started).Duration()
                    > TimeSpan.FromSeconds(1)
            )
            {
                return false;
            }

            return process.CloseMainWindow();
        }
        catch (Exception e)
            when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }
}
