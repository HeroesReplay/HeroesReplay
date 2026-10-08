using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.GameClient;

public enum SwitcherStopOutcome
{
    /// <summary>It exited after <c>CloseMainWindow</c>, or on its own.</summary>
    Closed,

    /// <summary>It was killed after the grace period and is gone.</summary>
    Killed,

    /// <summary>It is still running after the kill.</summary>
    StillRunning,
}

/// <summary>A HeroesSwitcher_x64 that had no Heroes child, and what closing it did.</summary>
public sealed record SwitcherStop(int Pid, SwitcherStopOutcome Outcome, string Detail = null)
{
    public string Describe()
    {
        string outcome = Outcome switch
        {
            SwitcherStopOutcome.Closed => "closed",
            SwitcherStopOutcome.Killed => "killed",
            _ => "still running",
        };
        return string.IsNullOrWhiteSpace(Detail)
            ? $"{NamedProcess.HeroesSwitcher} pid {Pid}: {outcome}."
            : $"{NamedProcess.HeroesSwitcher} pid {Pid}: {outcome} ({Detail}).";
    }
}

/// <summary>
/// A HeroesSwitcher_x64 left alone because its Heroes child is still running: a handoff, or a
/// game that did not close. Heroes is closed first.
/// </summary>
public sealed record SwitcherHandoff(int Pid, int HeroesPid)
{
    public string Describe() =>
        $"{NamedProcess.HeroesSwitcher} pid {Pid}: left running, its Heroes pid {HeroesPid} is still up.";
}

/// <summary>What <see cref="HeroesSwitcherShutdown.CloseIdle"/> did.</summary>
public sealed class SwitcherStopResult
{
    /// <summary>The switchers that had no Heroes child, with how each one ended.</summary>
    public IReadOnlyList<SwitcherStop> Stopped { get; init; } = Array.Empty<SwitcherStop>();

    /// <summary>The switchers whose Heroes child was still running at the end. None was closed.</summary>
    public IReadOnlyList<SwitcherHandoff> LeftAlone { get; init; } = Array.Empty<SwitcherHandoff>();

    /// <summary>True when every switcher without a Heroes child is gone.</summary>
    public bool AllStopped => Stopped.All(stop => stop.Outcome != SwitcherStopOutcome.StillRunning);

    /// <summary>One sentence per switcher, or an empty string when there was none.</summary>
    public string Describe() =>
        string.Join(
            " ",
            Stopped
                .Select(stop => stop.Describe())
                .Concat(LeftAlone.Select(handoff => handoff.Describe()))
        );
}

/// <summary>
/// After <c>services stop</c> closed Heroes of the Storm, closes each HeroesSwitcher_x64 that has
/// no Heroes child (#359): <c>CloseMainWindow</c>, then a kill when it is still there after
/// <see cref="DefaultGrace"/>. A switcher whose Heroes child is still running is left alone
/// (a handoff, or a game that did not close). That includes one that starts a Heroes before the
/// kill. Processes come from <see cref="ProcessTable"/>, so a reused pid is never closed.
/// </summary>
public sealed class HeroesSwitcherShutdown
{
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(5);

    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);
    private static readonly string SwitcherImage = NamedProcess.HeroesSwitcher + ".exe";
    private static readonly string HeroesImage = NamedProcess.HeroesOfTheStorm + ".exe";

    private readonly Func<IReadOnlyList<ProcessTableEntry>> snapshot;
    private readonly Func<ProcessTableEntry, bool> closeMainWindow;
    private readonly Func<ProcessTableEntry, ProcessKillResult> kill;
    private readonly Action<TimeSpan> wait;
    private readonly TimeSpan grace;

    public HeroesSwitcherShutdown()
        : this(
            ProcessTable.Snapshot,
            CloseMainWindow,
            ProcessTable.Kill,
            Thread.Sleep,
            DefaultGrace
        ) { }

    internal HeroesSwitcherShutdown(
        Func<IReadOnlyList<ProcessTableEntry>> snapshot,
        Func<ProcessTableEntry, bool> closeMainWindow,
        Func<ProcessTableEntry, ProcessKillResult> kill,
        Action<TimeSpan> wait,
        TimeSpan grace
    )
    {
        this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        this.closeMainWindow =
            closeMainWindow ?? throw new ArgumentNullException(nameof(closeMainWindow));
        this.kill = kill ?? throw new ArgumentNullException(nameof(kill));
        this.wait = wait ?? throw new ArgumentNullException(nameof(wait));
        this.grace = grace > TimeSpan.Zero ? grace : TimeSpan.Zero;
    }

    /// <summary>
    /// Closes the switchers that have no Heroes child. A switcher with no main window has nothing
    /// to answer <c>CloseMainWindow</c>, so it is killed without the wait.
    /// </summary>
    public SwitcherStopResult CloseIdle()
    {
        SwitcherPlan plan = Plan(snapshot());
        if (plan.Idle.Count == 0)
        {
            return new SwitcherStopResult { LeftAlone = plan.LeftAlone };
        }

        bool asked = false;
        foreach (ProcessTableEntry switcher in plan.Idle)
        {
            asked |= TryCloseMainWindow(switcher);
        }

        List<ProcessTableEntry> running = plan.Idle.ToList();
        TimeSpan waited = TimeSpan.Zero;
        while (asked && running.Count > 0 && waited < grace)
        {
            TimeSpan step = PollInterval < grace - waited ? PollInterval : grace - waited;
            wait(step);
            waited += step;
            running = StillRunning(running, snapshot());
        }

        var killed = new HashSet<int>();
        var detail = new Dictionary<int, string>();
        if (running.Count > 0)
        {
            // A switcher that started a Heroes during the wait is handing off now: leave it.
            HashSet<int> handingOff = Plan(snapshot())
                .LeftAlone.Select(handoff => handoff.Pid)
                .ToHashSet();
            foreach (ProcessTableEntry switcher in running)
            {
                if (handingOff.Contains(switcher.Pid))
                {
                    continue;
                }

                ProcessKillResult result = TryKill(switcher);
                if (result == ProcessKillResult.Killed)
                {
                    killed.Add(switcher.Pid);
                }
                else if (result != ProcessKillResult.Gone)
                {
                    detail[switcher.Pid] = "kill: " + result;
                }
            }
        }

        IReadOnlyList<ProcessTableEntry> after = snapshot();
        SwitcherPlan final = Plan(after);
        HashSet<int> leftAlone = final.LeftAlone.Select(handoff => handoff.Pid).ToHashSet();
        var stopped = new List<SwitcherStop>();
        foreach (ProcessTableEntry switcher in plan.Idle)
        {
            bool alive = IsAlive(switcher, after);
            if (alive && leftAlone.Contains(switcher.Pid))
            {
                continue;
            }

            SwitcherStopOutcome outcome =
                alive ? SwitcherStopOutcome.StillRunning
                : killed.Contains(switcher.Pid) ? SwitcherStopOutcome.Killed
                : SwitcherStopOutcome.Closed;
            detail.TryGetValue(switcher.Pid, out string why);
            stopped.Add(
                new SwitcherStop(
                    switcher.Pid,
                    outcome,
                    outcome == SwitcherStopOutcome.StillRunning ? why : null
                )
            );
        }

        return new SwitcherStopResult { Stopped = stopped, LeftAlone = final.LeftAlone };
    }

    /// <summary>
    /// Splits the switchers in <paramref name="table"/>: those with no running Heroes child, and
    /// those whose Heroes child is up. A Heroes counts as a switcher's child only when its parent
    /// pid is that switcher and it did not start before it (a reused pid is not a parent).
    /// </summary>
    internal static SwitcherPlan Plan(IReadOnlyList<ProcessTableEntry> table)
    {
        table ??= Array.Empty<ProcessTableEntry>();
        var switchers = table.Where(process => IsImage(process, SwitcherImage)).ToList();
        var byPid = new Dictionary<int, ProcessTableEntry>();
        foreach (ProcessTableEntry switcher in switchers)
        {
            byPid.TryAdd(switcher.Pid, switcher);
        }

        var heroesChild = new SortedDictionary<int, int>();
        foreach (ProcessTableEntry heroes in table.Where(process => IsImage(process, HeroesImage)))
        {
            if (
                byPid.TryGetValue(heroes.ParentPid, out ProcessTableEntry parent)
                && !StartedBefore(heroes, parent)
            )
            {
                heroesChild.TryAdd(parent.Pid, heroes.Pid);
            }
        }

        return new SwitcherPlan(
            switchers.Where(switcher => !heroesChild.ContainsKey(switcher.Pid)).ToList(),
            heroesChild.Select(pair => new SwitcherHandoff(pair.Key, pair.Value)).ToList()
        );
    }

    private bool TryCloseMainWindow(ProcessTableEntry switcher)
    {
        try
        {
            return closeMainWindow(switcher);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private ProcessKillResult TryKill(ProcessTableEntry switcher)
    {
        try
        {
            return kill(switcher);
        }
        catch (Exception)
        {
            return ProcessKillResult.Failed;
        }
    }

    private static List<ProcessTableEntry> StillRunning(
        IEnumerable<ProcessTableEntry> switchers,
        IReadOnlyList<ProcessTableEntry> table
    ) => switchers.Where(switcher => IsAlive(switcher, table)).ToList();

    /// <summary>The same process (pid and, when known, start time) is still in the table.</summary>
    private static bool IsAlive(
        ProcessTableEntry switcher,
        IReadOnlyList<ProcessTableEntry> table
    ) =>
        (table ?? Array.Empty<ProcessTableEntry>()).Any(process =>
            process.Pid == switcher.Pid
            && IsImage(process, SwitcherImage)
            && (
                switcher.StartTime == null
                || process.StartTime == null
                || process.StartTime == switcher.StartTime
            )
        );

    private static bool StartedBefore(ProcessTableEntry child, ProcessTableEntry parent) =>
        child.StartTime is DateTimeOffset childStarted
        && parent.StartTime is DateTimeOffset parentStarted
        && childStarted < parentStarted;

    private static bool IsImage(ProcessTableEntry process, string image)
    {
        if (process == null)
        {
            return false;
        }

        string name = string.IsNullOrWhiteSpace(process.ImagePath)
            ? process.Name
            : Path.GetFileName(process.ImagePath);
        return string.Equals(name, image, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Asks the switcher's main window to close. False when it has none, is gone, or the pid now
    /// belongs to another process.
    /// </summary>
    private static bool CloseMainWindow(ProcessTableEntry switcher)
    {
        try
        {
            using Process process = Process.GetProcessById(switcher.Pid);
            if (
                switcher.StartTime is DateTimeOffset started
                && (new DateTimeOffset(process.StartTime) - started).Duration() > StartTimeTolerance
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

internal sealed record SwitcherPlan(
    IReadOnlyList<ProcessTableEntry> Idle,
    IReadOnlyList<SwitcherHandoff> LeftAlone
);
