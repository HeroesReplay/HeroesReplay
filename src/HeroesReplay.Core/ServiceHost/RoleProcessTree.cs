using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// A role's process tree (#409): what a kill takes, the role first and every parent before its
/// children, and the obs64 in it that the kill leaves running.
/// </summary>
public sealed record RoleTreePlan(
    IReadOnlyList<ProcessTableEntry> Kill,
    IReadOnlyList<ProcessTableEntry> Spare
);

/// <summary>
/// What a role kill did (#409): the processes it killed, the obs64 it left running, and the
/// processes it could not kill.
/// </summary>
public sealed record RoleTreeKill(
    IReadOnlyList<ProcessTableEntry> Killed,
    IReadOnlyList<ProcessTableEntry> Spared,
    IReadOnlyList<ProcessTableEntry> Failed
);

/// <summary>
/// Kills a role's process tree, but never OBS (#409). HeroesReplay starts OBS detached
/// (<see cref="ObsLauncher"/>), yet an OBS that an older build started, or that someone started
/// from a role's console, is still a descendant of that role, and a tree kill of a stale
/// spectate or of <c>services stop</c> forcing a role would take the live stream down with it.
/// So every obs64 in the tree is left running with everything it started (its browser pages,
/// its recording muxer), the rest is killed, the role first, and one warning names what was
/// spared. A child is a process whose parent pid is the parent's and that started after it, so
/// a process whose parent died and whose pid was reused is not taken for its child. Each kill
/// checks the start time again, so a reused pid is never killed.
/// </summary>
public sealed class RoleProcessTree
{
    public const string ObsSparedCode = "service.role_kill_spared_obs";

    private static readonly string ObsImage = ObsLaunchDecision.ProcessName + ".exe";

    private readonly IProcessTable table;
    private readonly ILogger logger;

    public RoleProcessTree(IProcessTable table, ILogger logger = null)
    {
        this.table = table ?? throw new ArgumentNullException(nameof(table));
        this.logger = logger ?? NullLogger.Instance;
    }

    /// <summary>The tree of <paramref name="pid"/> in <paramref name="entries"/>, or null when it is not there.</summary>
    public static RoleTreePlan Plan(IReadOnlyList<ProcessTableEntry> entries, int pid)
    {
        entries ??= Array.Empty<ProcessTableEntry>();
        ProcessTableEntry root = entries.FirstOrDefault(entry => entry.Pid == pid);
        if (root == null)
        {
            return null;
        }

        var kill = new List<ProcessTableEntry> { root };
        var spare = new List<ProcessTableEntry>();
        var seen = new HashSet<int> { root.Pid };
        var pending = new Queue<ProcessTableEntry>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            ProcessTableEntry parent = pending.Dequeue();
            foreach (ProcessTableEntry child in entries.Where(entry => IsChild(parent, entry)))
            {
                if (!seen.Add(child.Pid))
                {
                    continue;
                }

                if (IsObs(child))
                {
                    // OBS and everything it started stay up.
                    spare.Add(child);
                    continue;
                }

                kill.Add(child);
                pending.Enqueue(child);
            }
        }

        return new RoleTreePlan(kill, spare);
    }

    /// <summary>
    /// Kills <paramref name="pid"/> and its tree, except obs64. Throws when the pid is not
    /// running, or when the role itself could not be killed; the rest of the tree is still tried.
    /// </summary>
    public RoleTreeKill Kill(int pid)
    {
        RoleTreePlan plan =
            Plan(table.Snapshot(), pid)
            ?? throw new ArgumentException(
                $"Process with an Id of {pid} is not running.",
                nameof(pid)
            );
        var killed = new List<ProcessTableEntry>();
        var failed = new List<ProcessTableEntry>();
        ProcessKillResult? rootFailure = null;
        foreach (ProcessTableEntry target in plan.Kill)
        {
            ProcessKillResult result;
            try
            {
                result = table.Kill(target);
            }
            catch (Exception)
            {
                result = ProcessKillResult.Failed;
            }

            if (result == ProcessKillResult.Killed)
            {
                killed.Add(target);
            }
            else if (result is not (ProcessKillResult.Gone or ProcessKillResult.Replaced))
            {
                failed.Add(target);
                if (target.Pid == pid)
                {
                    rootFailure = result;
                }
            }
        }

        if (plan.Spare.Count > 0)
        {
            logger.LogWarning(
                "Killed {Name} pid {Pid} and {Descendants} process(es) it started, but left {Obs} running: OBS is never killed with a role, so the stream stays up [{Code}].",
                plan.Kill[0].Name,
                pid,
                plan.Kill.Count - 1,
                Names(plan.Spare),
                ObsSparedCode
            );
        }

        if (rootFailure != null)
        {
            throw new InvalidOperationException($"Could not kill pid {pid}: {rootFailure}.");
        }

        return new RoleTreeKill(killed, plan.Spare, failed);
    }

    /// <summary>The spared line for a console without a logger (<c>services stop</c>), or null.</summary>
    public static string Describe(int pid, RoleTreeKill kill) =>
        kill == null || kill.Spared.Count == 0
            ? null
            : $"Left {Names(kill.Spared)} running in pid {pid}'s process tree: OBS is never killed with a role, so the stream stays up [{ObsSparedCode}].";

    private static string Names(IEnumerable<ProcessTableEntry> entries) =>
        string.Join(", ", entries.Select(entry => $"{entry.Name} pid {entry.Pid}"));

    private static bool IsChild(ProcessTableEntry parent, ProcessTableEntry entry) =>
        entry.ParentPid == parent.Pid
        && entry.Pid != parent.Pid
        && entry.StartTime is DateTimeOffset childStarted
        && parent.StartTime is DateTimeOffset parentStarted
        && childStarted >= parentStarted;

    private static bool IsObs(ProcessTableEntry entry) =>
        string.Equals(entry.Name, ObsImage, StringComparison.OrdinalIgnoreCase);
}
