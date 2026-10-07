using System;

namespace HeroesReplay.Core.GameClient;

/// <summary>
/// The leftover Battle.net <c>Agent.exe</c> reaper (#251). Bound from the <c>BattleNetAgents</c>
/// section.
/// </summary>
public sealed class BattleNetAgentSettings
{
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromMinutes(2);
    public const int DefaultWarnAboveCount = 5;

    /// <summary>
    /// Reap leftover agents when spectate starts and before each Battle.net launch. False leaves
    /// every Agent.exe alone.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// An agent younger than this is never reaped, so a launch still in progress keeps the agent
    /// it just started.
    /// </summary>
    public TimeSpan GracePeriod { get; set; } = DefaultGracePeriod;

    /// <summary>A warning names the count when more Battle.net agents than this are running.</summary>
    public int WarnAboveCount { get; set; } = DefaultWarnAboveCount;

    public TimeSpan Grace => GracePeriod > TimeSpan.Zero ? GracePeriod : DefaultGracePeriod;
}
