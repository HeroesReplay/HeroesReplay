using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.GameClient;

/// <summary>One process the reaper terminates, and why.</summary>
public sealed record BattleNetAgentReap(ProcessTableEntry Process, string Reason);

/// <summary>
/// What <see cref="BattleNetAgentSelection.Select"/> decided: the Battle.net agents seen, the one
/// kept, and the processes to terminate (agents first, then their console hosts).
/// </summary>
public sealed record BattleNetAgentPlan(
    int AgentCount,
    ProcessTableEntry Kept,
    string KeptReason,
    IReadOnlyList<BattleNetAgentReap> Reap
)
{
    public static readonly BattleNetAgentPlan None = new(
        0,
        null,
        null,
        Array.Empty<BattleNetAgentReap>()
    );
}

/// <summary>
/// Which Battle.net <c>Agent.exe</c> processes are leftovers (#251). Every
/// <c>Battle.net.exe --exec="launch Hero"</c> leaves a new agent that never exits. Battle.net's
/// own agent is kept: the oldest one a running Battle.net.exe started at least the grace period
/// ago, or the oldest agent when there is none.
/// Every other agent older than the grace period is reaped, with a <c>conhost.exe</c> that a
/// reaped agent started. Nothing else is ever selected.
/// </summary>
public static class BattleNetAgentSelection
{
    public const string AgentImage = "Agent.exe";
    public const string ConsoleHostImage = "conhost.exe";
    public const string BattleNetImage = "Battle.net.exe";

    /// <summary><c>%ProgramData%\Battle.net\Agent</c>.</summary>
    public static string DefaultAgentRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Battle.net",
            "Agent"
        );

    /// <summary>An <c>Agent.exe</c> whose image sits under <paramref name="agentRoot"/>.</summary>
    public static bool IsAgent(ProcessTableEntry process, string agentRoot)
    {
        if (
            process == null
            || string.IsNullOrWhiteSpace(process.ImagePath)
            || string.IsNullOrWhiteSpace(agentRoot)
        )
        {
            return false;
        }

        if (
            !string.Equals(
                Path.GetFileName(process.ImagePath),
                AgentImage,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return false;
        }

        string root = Normalize(agentRoot);
        string image = Normalize(process.ImagePath);
        return root != null
            && image != null
            && image.StartsWith(
                root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            );
    }

    public static BattleNetAgentPlan Select(
        IReadOnlyList<ProcessTableEntry> processes,
        string agentRoot,
        DateTimeOffset now,
        TimeSpan grace
    )
    {
        if (processes == null || processes.Count == 0)
        {
            return BattleNetAgentPlan.None;
        }

        var byPid = new Dictionary<int, ProcessTableEntry>();
        foreach (ProcessTableEntry process in processes)
        {
            if (process != null)
            {
                byPid.TryAdd(process.Pid, process);
            }
        }

        List<ProcessTableEntry> agents = processes
            .Where(process => IsAgent(process, agentRoot))
            .OrderBy(process => process.StartTime ?? DateTimeOffset.MaxValue)
            .ThenBy(process => process.Pid)
            .ToList();
        if (agents.Count == 0)
        {
            return BattleNetAgentPlan.None;
        }

        // Only a settled agent counts as Battle.net's own: during a launch the short-lived
        // `Battle.net.exe --exec` is still the parent of the agent it just started.
        ProcessTableEntry kept = agents.FirstOrDefault(agent =>
            agent.StartTime is DateTimeOffset started
            && now - started >= grace
            && IsImage(LiveParent(agent, byPid), BattleNetImage)
        );
        string keptReason;
        if (kept != null)
        {
            keptReason = $"started by Battle.net.exe pid {kept.ParentPid}";
        }
        else
        {
            kept = agents[0];
            keptReason =
                "the oldest Battle.net agent; no settled agent has a running Battle.net.exe parent";
        }

        var reap = new List<BattleNetAgentReap>();
        var reapedAgents = new List<ProcessTableEntry>();
        foreach (ProcessTableEntry agent in agents)
        {
            if (ReferenceEquals(agent, kept) || agent.StartTime is not DateTimeOffset started)
            {
                continue;
            }

            if (now - started < grace)
            {
                continue;
            }

            ProcessTableEntry parent = LiveParent(agent, byPid);
            string reason =
                parent == null
                    ? $"orphan: parent pid {agent.ParentPid} is gone"
                    : $"extra: Battle.net keeps agent pid {kept.Pid}; parent {parent.Name} pid {parent.Pid} still runs";
            reap.Add(new BattleNetAgentReap(agent, reason));
            reapedAgents.Add(agent);
        }

        foreach (ProcessTableEntry agent in reapedAgents)
        {
            foreach (ProcessTableEntry process in processes)
            {
                if (
                    process != null
                    && process.ParentPid == agent.Pid
                    && IsImage(process, ConsoleHostImage)
                    && process.StartTime is DateTimeOffset hostStarted
                    && hostStarted >= agent.StartTime
                )
                {
                    reap.Add(
                        new BattleNetAgentReap(
                            process,
                            $"console host of reaped Agent.exe pid {agent.Pid}"
                        )
                    );
                }
            }
        }

        return new BattleNetAgentPlan(agents.Count, kept, keptReason, reap);
    }

    /// <summary>
    /// The parent while it still runs. A parent pid whose process started after the child is a
    /// reused pid, so the real parent is gone.
    /// </summary>
    private static ProcessTableEntry LiveParent(
        ProcessTableEntry child,
        IReadOnlyDictionary<int, ProcessTableEntry> byPid
    )
    {
        if (
            child.ParentPid <= 0
            || child.ParentPid == child.Pid
            || !byPid.TryGetValue(child.ParentPid, out ProcessTableEntry parent)
        )
        {
            return null;
        }

        if (
            parent.StartTime is DateTimeOffset parentStarted
            && child.StartTime is DateTimeOffset childStarted
            && parentStarted > childStarted
        )
        {
            return null;
        }

        return parent;
    }

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

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}
