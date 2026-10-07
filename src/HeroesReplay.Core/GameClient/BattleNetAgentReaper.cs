using System;
using System.Collections.Generic;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.GameClient;

/// <summary>
/// Terminates the Battle.net <c>Agent.exe</c> processes that current-patch launches leave behind
/// (#251), as <see cref="BattleNetAgentSelection"/> picks them. Runs when spectate starts and
/// before each Battle.net launch. It never throws: a failed reap is logged and the launch goes on.
/// </summary>
public sealed class BattleNetAgentReaper
{
    private readonly BattleNetAgentSettings settings;
    private readonly ILogger<BattleNetAgentReaper> logger;
    private readonly Func<IReadOnlyList<ProcessTableEntry>> snapshot;
    private readonly Func<ProcessTableEntry, ProcessKillResult> kill;
    private readonly TimeProvider time;
    private readonly string agentRoot;
    private bool accessDeniedLogged;

    public BattleNetAgentReaper(AppSettings settings, ILogger<BattleNetAgentReaper> logger)
        : this(
            settings?.BattleNetAgents,
            logger,
            ProcessTable.Snapshot,
            ProcessTable.Kill,
            TimeProvider.System,
            BattleNetAgentSelection.DefaultAgentRoot
        ) { }

    internal BattleNetAgentReaper(
        BattleNetAgentSettings settings,
        ILogger<BattleNetAgentReaper> logger,
        Func<IReadOnlyList<ProcessTableEntry>> snapshot,
        Func<ProcessTableEntry, ProcessKillResult> kill,
        TimeProvider time,
        string agentRoot
    )
    {
        this.settings = settings ?? new BattleNetAgentSettings();
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        this.kill = kill ?? throw new ArgumentNullException(nameof(kill));
        this.time = time ?? TimeProvider.System;
        this.agentRoot = agentRoot;
    }

    /// <summary>Reaps the leftover agents. Returns how many processes were terminated.</summary>
    public int Reap(string when)
    {
        if (!settings.Enabled)
        {
            return 0;
        }

        try
        {
            BattleNetAgentPlan plan = BattleNetAgentSelection.Select(
                snapshot(),
                agentRoot,
                time.GetUtcNow(),
                settings.Grace
            );
            if (plan.AgentCount > settings.WarnAboveCount)
            {
                logger.LogWarning(
                    "{Count} Battle.net Agent.exe processes are running ({When}), above {Limit}. Reaping {Reap} of them.",
                    plan.AgentCount,
                    when,
                    settings.WarnAboveCount,
                    plan.Reap.Count
                );
            }

            if (plan.Reap.Count == 0)
            {
                return 0;
            }

            int reaped = 0;
            foreach (BattleNetAgentReap item in plan.Reap)
            {
                if (Terminate(item))
                {
                    reaped++;
                }
            }

            logger.LogInformation(
                "Reaped {Reaped} of {Planned} leftover Battle.net processes ({When}). Kept Agent.exe pid {KeptPid}, {KeptReason}.",
                reaped,
                plan.Reap.Count,
                when,
                plan.Kept?.Pid,
                plan.KeptReason
            );
            return reaped;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not reap leftover Battle.net agents ({When}).", when);
            return 0;
        }
    }

    private bool Terminate(BattleNetAgentReap item)
    {
        ProcessTableEntry process = item.Process;
        ProcessKillResult result;
        try
        {
            result = kill(process);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not terminate {Name} pid {Pid}.", process.Name, process.Pid);
            return false;
        }

        switch (result)
        {
            case ProcessKillResult.Killed:
                logger.LogInformation(
                    "Reaped {Name} pid {Pid} started {StartTime:O}: {Reason}.",
                    process.Name,
                    process.Pid,
                    process.StartTime,
                    item.Reason
                );
                return true;
            case ProcessKillResult.AccessDenied when !accessDeniedLogged:
                accessDeniedLogged = true;
                logger.LogInformation(
                    "Could not terminate {Name} pid {Pid}: access denied. Later denials are logged at Debug.",
                    process.Name,
                    process.Pid
                );
                return false;
            default:
                logger.LogDebug(
                    "Did not terminate {Name} pid {Pid}: {Result}.",
                    process.Name,
                    process.Pid,
                    result
                );
                return false;
        }
    }
}
