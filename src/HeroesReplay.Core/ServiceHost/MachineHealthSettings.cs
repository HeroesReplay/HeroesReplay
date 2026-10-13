using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// The machine section of <c>services status</c> and the supervisor's hourly health line (#251,
/// #399). Bound from the <c>MachineHealth</c> section. A value above a limit is a warning; it does
/// not change a role's state or the exit code.
/// </summary>
public sealed class MachineHealthSettings
{
    public static readonly IReadOnlyList<string> DefaultWatchedProcesses = new[]
    {
        "heroesreplay",
        "obs64",
        "aspire-managed",
    };

    /// <summary>Physical memory in use, percent of installed.</summary>
    public double MemoryWarnPercent { get; set; } = 90;

    /// <summary>Commit charge, percent of the commit limit (RAM plus page file).</summary>
    public double CommitWarnPercent { get; set; } = 85;

    /// <summary>Battle.net <c>Agent.exe</c> processes. Battle.net itself needs one.</summary>
    public int AgentWarnCount { get; set; } = 5;

    public int ConhostWarnCount { get; set; } = 40;

    /// <summary>
    /// <c>HeroesOfTheStorm_x64.exe</c> processes. Two is the HeroesSwitcher handoff; more is a leak.
    /// </summary>
    public int HeroesWarnCount { get; set; } = 2;

    /// <summary>How often the supervisor logs the machine and the watched processes' private bytes.</summary>
    public TimeSpan LogInterval { get; set; } = TimeSpan.FromHours(1);

    public const int DefaultTopConsumerCount = 5;

    /// <summary>
    /// While physical memory or commit is above its limit, the machine line names this many
    /// processes with the most private bytes (commit), whoever owns them (#399). Report only:
    /// HeroesReplay never stops or changes them. Zero or less names none.
    /// </summary>
    public int TopConsumerCount { get; set; } = DefaultTopConsumerCount;

    /// <summary>
    /// Process names (no <c>.exe</c>) whose private bytes the supervisor logs, so steady growth
    /// shows up. Null or empty means heroesreplay, obs64, and aspire-managed.
    /// </summary>
    public List<string> WatchedProcesses { get; set; }

    public IReadOnlyList<string> Watched
    {
        get
        {
            List<string> configured = WatchedProcesses
                ?.Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
            return configured is { Count: > 0 } ? configured : DefaultWatchedProcesses;
        }
    }
}
