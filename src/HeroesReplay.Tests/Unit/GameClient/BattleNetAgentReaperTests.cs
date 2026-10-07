using System;
using System.Collections.Generic;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class BattleNetAgentReaperTests
{
    private const string Root = @"C:\ProgramData\Battle.net\Agent";
    private const string AgentPath = @"C:\ProgramData\Battle.net\Agent\Agent.9824\Agent.exe";
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 13, 30, 0, TimeSpan.Zero);

    private readonly List<ProcessTableEntry> table = new()
    {
        new(200, 9000, "Agent.exe", AgentPath, Now.AddDays(-4)),
        new(300, 9001, "Agent.exe", AgentPath, Now.AddHours(-2)),
        new(301, 9002, "Agent.exe", AgentPath, Now.AddHours(-1)),
    };

    [Fact]
    public void Reap_KillsThePlannedLeftoversAndCountsOnlyKilledOnes()
    {
        var killed = new List<int>();
        BattleNetAgentReaper reaper = Reaper(
            new BattleNetAgentSettings(),
            process =>
            {
                killed.Add(process.Pid);
                return process.Pid == 301
                    ? ProcessKillResult.AccessDenied
                    : ProcessKillResult.Killed;
            }
        );

        int reaped = reaper.Reap("test");

        Assert.Equal(new[] { 300, 301 }, killed);
        Assert.Equal(1, reaped);
    }

    [Fact]
    public void Reap_DoesNothingWhenDisabled()
    {
        var killed = new List<int>();
        BattleNetAgentReaper reaper = Reaper(
            new BattleNetAgentSettings { Enabled = false },
            process =>
            {
                killed.Add(process.Pid);
                return ProcessKillResult.Killed;
            }
        );

        Assert.Equal(0, reaper.Reap("test"));
        Assert.Empty(killed);
    }

    [Fact]
    public void Reap_NeverThrowsWhenTheSnapshotFails()
    {
        var reaper = new BattleNetAgentReaper(
            new BattleNetAgentSettings(),
            NullLogger<BattleNetAgentReaper>.Instance,
            () => throw new InvalidOperationException("no snapshot"),
            _ => ProcessKillResult.Killed,
            new FixedClock(Now),
            Root
        );

        Assert.Equal(0, reaper.Reap("test"));
    }

    private BattleNetAgentReaper Reaper(
        BattleNetAgentSettings settings,
        Func<ProcessTableEntry, ProcessKillResult> kill
    ) =>
        new(
            settings,
            NullLogger<BattleNetAgentReaper>.Instance,
            () => table,
            kill,
            new FixedClock(Now),
            Root
        );

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FixedClock(DateTimeOffset now)
        {
            this.now = now;
        }

        public override DateTimeOffset GetUtcNow() => now;
    }
}
