using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// How a process ran: its pid (null when it did not start), whether it exited within the wait,
/// and its exit code then.
/// </summary>
public sealed record ProcessRun(int? Pid, bool Exited, int? ExitCode)
{
    public static readonly ProcessRun NotStarted = new(null, false, null);
}

/// <summary>
/// Starting a process as a port (#409), so the OBS launch is tested without starting one. The
/// real one is <see cref="WindowsProcessStarter"/>.
/// </summary>
public interface IProcessStarter
{
    /// <summary>Starts the process and waits at most <paramref name="wait"/> for it to exit.</summary>
    ProcessRun Run(ProcessStartInfo start, TimeSpan wait);
}

/// <summary><see cref="Process.Start(ProcessStartInfo)"/>, then a bounded wait for the exit.</summary>
internal sealed class WindowsProcessStarter : IProcessStarter
{
    public ProcessRun Run(ProcessStartInfo start, TimeSpan wait)
    {
        using Process process = Process.Start(start);
        if (process == null)
        {
            return ProcessRun.NotStarted;
        }

        bool exited = process.WaitForExit(wait);
        return new ProcessRun(process.Id, exited, exited ? process.ExitCode : null);
    }
}

public enum ObsLaunchOutcome
{
    /// <summary>A new obs64 runs; <see cref="ObsLaunch.Obs"/> is it.</summary>
    Started,

    /// <summary>An obs64 already ran, so nothing was started. It is not owned.</summary>
    AlreadyRunning,
    Failed,
}

/// <summary>
/// What <see cref="ObsLauncher.Launch"/> did. <see cref="Obs"/> is the new obs64 (pid and start
/// time) after a start, the one that already ran otherwise, null on a failure.
/// </summary>
public sealed record ObsLaunch(ObsLaunchOutcome Outcome, ProcessTableEntry Obs, string Detail)
{
    public static ObsLaunch Failed(string detail) => new(ObsLaunchOutcome.Failed, null, detail);
}

/// <summary>
/// The one way HeroesReplay starts OBS (#409): spectate's own launch (<c>WindowsObsProcess</c>)
/// and the supervisor's OBS watchdog (#398) both call it. Inside <see cref="ObsLaunchGate"/>, and
/// only when no obs64 runs, it deletes OBS's stale crash sentinels
/// (<see cref="ObsCrashSentinel.ForThisUser"/>) and starts OBS through
/// <c>cmd /d /c start "" /D &lt;dir&gt; &lt;obs64&gt; &lt;arguments&gt;</c>. OBS's parent is
/// then a <c>cmd</c> that exits at once, never a role, so killing a role's process tree (a stale
/// spectate, <c>services stop</c> forcing it) does not take OBS and the stream with it. The new
/// obs64 is the one that started at or after the launch, newest first; its pid and start time
/// are what the caller owns. Every step that touches the machine is a port, so tests use fakes.
/// </summary>
public sealed class ObsLauncher
{
    /// <summary>How long <c>cmd /c start</c> may take to return.</summary>
    public static readonly TimeSpan DefaultLauncherWait = TimeSpan.FromSeconds(15);

    /// <summary>How long the new obs64 may take to show in the process table after that.</summary>
    public static readonly TimeSpan DefaultAppearWait = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>A process start time and this clock differ by the clock's tick at most.</summary>
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);

    private static readonly string ObsImage = ObsLaunchDecision.ProcessName + ".exe";

    private readonly IProcessStarter starter;
    private readonly IProcessTable table;
    private readonly ObsCrashSentinel sentinel;
    private readonly ILogger logger;

    public ObsLauncher(
        IProcessStarter starter,
        IProcessTable table,
        ObsCrashSentinel sentinel,
        ILogger logger
    )
    {
        this.starter = starter ?? throw new ArgumentNullException(nameof(starter));
        this.table = table ?? throw new ArgumentNullException(nameof(table));
        // No sentinel means none is cleared.
        this.sentinel = sentinel;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The cross-process gate. Tests give a name of their own.</summary>
    public string GateName { get; init; } = ObsLaunchGate.MutexName;
    public TimeSpan GateWait { get; init; } = ObsLaunchGate.DefaultWait;
    public TimeSpan LauncherWait { get; init; } = DefaultLauncherWait;
    public TimeSpan AppearWait { get; init; } = DefaultAppearWait;
    public TimeSpan PollInterval { get; init; } = DefaultPollInterval;
    public Action<TimeSpan> Wait { get; init; } = Thread.Sleep;
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;
    public Func<string, bool> ExecutableExists { get; init; } = File.Exists;

    /// <summary>This user's launcher: the real process table, <c>cmd.exe</c>, and sentinel folder.</summary>
    public static ObsLauncher ForThisUser(ILogger logger) =>
        new(
            new WindowsProcessStarter(),
            WindowsProcessTable.Instance,
            ObsCrashSentinel.ForThisUser(logger),
            logger
        );

    /// <summary>
    /// <c>/d /c start "" /D "&lt;dir&gt;" "&lt;obs64&gt;" &lt;arguments&gt;</c>: <c>start</c>
    /// creates OBS and returns, so <c>cmd</c> exits and OBS keeps a dead parent.
    /// </summary>
    public static string CommandArguments(string path, string arguments)
    {
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string command = $"/d /c start \"\" /D \"{directory}\" \"{path}\"";
        return string.IsNullOrWhiteSpace(arguments) ? command : command + " " + arguments;
    }

    /// <summary>Starts OBS detached, or says why not. Never throws.</summary>
    public ObsLaunch Launch(string path, string arguments)
    {
        try
        {
            return LaunchInsideGate(path, arguments);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "OBS was not started.");
            return ObsLaunch.Failed(e.Message);
        }
    }

    private ObsLaunch LaunchInsideGate(string path, string arguments)
    {
        if (string.IsNullOrWhiteSpace(path) || !ExecutableExists(path))
        {
            return ObsLaunch.Failed($"OBS was not found at `{path}`. Set OBS:ExecutablePath.");
        }

        // spectate and the supervisor's watchdog check and start inside one gate: never two OBS.
        using ObsLaunchGate gate = ObsLaunchGate.Enter(GateWait, GateName);
        if (!gate.Held)
        {
            return ObsLaunch.Failed("another OBS launch held the launch gate.");
        }

        DateTimeOffset since = Now();
        ProcessTableEntry running = ObsProcesses(table.Snapshot())
            .OrderBy(entry => entry.StartTime ?? DateTimeOffset.MaxValue)
            .FirstOrDefault();
        if (running != null)
        {
            logger.LogInformation(
                "OBS pid {Pid} is already running, so no OBS was started. It is not owned.",
                running.Pid
            );
            return new ObsLaunch(
                ObsLaunchOutcome.AlreadyRunning,
                running,
                $"OBS pid {running.Pid} is already running."
            );
        }

        RemoveStaleSentinels();
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            Arguments = CommandArguments(path, arguments),
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory,
        };
        ProcessRun run = starter.Run(start, LauncherWait) ?? ProcessRun.NotStarted;
        if (run.Pid == null)
        {
            return ObsLaunch.Failed("cmd.exe did not start.");
        }

        if (!run.Exited)
        {
            return ObsLaunch.Failed(
                $"cmd.exe did not return within {LauncherWait.TotalSeconds:0}s."
            );
        }

        if (run.ExitCode != 0)
        {
            return ObsLaunch.Failed($"cmd.exe exited {run.ExitCode}.");
        }

        ProcessTableEntry started = WaitForNewObs(since);
        if (started == null)
        {
            return ObsLaunch.Failed(
                $"no new obs64 appeared within {AppearWait.TotalSeconds:0}s of `cmd /c start`."
            );
        }

        logger.LogInformation(
            "Started OBS detached: obs64 pid {Pid} (started {StartedAt:O}), parent pid {ParentPid} (cmd /c start, pid {Cmd}, exited), not a child of this process (pid {Self}). `{Path}` {Arguments}",
            started.Pid,
            started.StartTime,
            started.ParentPid,
            run.Pid,
            Environment.ProcessId,
            path,
            arguments
        );
        return new ObsLaunch(
            ObsLaunchOutcome.Started,
            started,
            $"OBS pid {started.Pid} started detached (cmd /c start)."
        );
    }

    /// <summary>
    /// The obs64 that started at or after the launch, newest start time first, so neither an
    /// older OBS nor a reused pid is taken for it. Null when none shows within the wait.
    /// </summary>
    private ProcessTableEntry WaitForNewObs(DateTimeOffset since)
    {
        TimeSpan poll = PollInterval > TimeSpan.Zero ? PollInterval : DefaultPollInterval;
        int polls = Math.Max(1, (int)Math.Ceiling(AppearWait / poll));
        for (int attempt = 0; ; attempt++)
        {
            ProcessTableEntry newest = ObsProcesses(table.Snapshot())
                .Where(entry => entry.StartTime >= since - StartTimeTolerance)
                .OrderByDescending(entry => entry.StartTime)
                .FirstOrDefault();
            if (newest != null || attempt >= polls)
            {
                return newest;
            }

            Wait?.Invoke(poll);
        }
    }

    private void RemoveStaleSentinels()
    {
        try
        {
            sentinel?.RemoveStale();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not clear stale OBS crash sentinels before the launch.");
        }
    }

    private static IEnumerable<ProcessTableEntry> ObsProcesses(
        IReadOnlyList<ProcessTableEntry> entries
    ) =>
        (entries ?? Array.Empty<ProcessTableEntry>()).Where(entry =>
            string.Equals(entry.Name, ObsImage, StringComparison.OrdinalIgnoreCase)
        );
}
