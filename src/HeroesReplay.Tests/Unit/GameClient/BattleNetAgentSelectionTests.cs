using System;
using System.Linq;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class BattleNetAgentSelectionTests
{
    private const string Root = @"C:\ProgramData\Battle.net\Agent";
    private const string AgentPath = @"C:\ProgramData\Battle.net\Agent\Agent.9824\Agent.exe";
    private const string BattleNetPath = @"C:\heroesreplay\Battle.net\Battle.net.exe";
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 13, 30, 0, TimeSpan.Zero);
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    [Fact]
    public void Select_KeepsTheAgentBattleNetStartedAndReapsOrphans()
    {
        ProcessTableEntry battleNet = Process(100, 1, BattleNetPath, Now.AddDays(-5));
        ProcessTableEntry main = Process(200, 100, AgentPath, Now.AddDays(-5));
        ProcessTableEntry orphanA = Process(300, 9001, AgentPath, Now.AddHours(-3));
        ProcessTableEntry orphanB = Process(301, 9002, AgentPath, Now.AddHours(-1));

        BattleNetAgentPlan plan = Select(battleNet, orphanA, main, orphanB);

        Assert.Equal(3, plan.AgentCount);
        Assert.Same(main, plan.Kept);
        Assert.Equal(new[] { 300, 301 }, Pids(plan));
        Assert.All(plan.Reap, item => Assert.StartsWith("orphan", item.Reason));
    }

    [Fact]
    public void Select_KeepsTheOldestAgentWhenNoneHasABattleNetParent()
    {
        ProcessTableEntry oldest = Process(200, 9000, AgentPath, Now.AddDays(-7));
        ProcessTableEntry newer = Process(300, 9001, AgentPath, Now.AddHours(-2));
        ProcessTableEntry newest = Process(301, 9002, AgentPath, Now.AddMinutes(-30));

        BattleNetAgentPlan plan = Select(newest, newer, oldest);

        Assert.Same(oldest, plan.Kept);
        Assert.Equal(new[] { 300, 301 }, Pids(plan));
    }

    [Fact]
    public void Select_KeepsAgentsYoungerThanTheGracePeriod()
    {
        ProcessTableEntry main = Process(200, 9000, AgentPath, Now.AddDays(-1));
        ProcessTableEntry launching = Process(300, 9001, AgentPath, Now.AddSeconds(-90));
        ProcessTableEntry old = Process(301, 9002, AgentPath, Now.AddMinutes(-2));

        BattleNetAgentPlan plan = Select(main, launching, old);

        Assert.Equal(new[] { 301 }, Pids(plan));
    }

    [Fact]
    public void Select_DoesNotKeepTheAgentAnInProgressLaunchJustStarted()
    {
        // The `--exec` Battle.net.exe is still alive and parents the agent it started 10 s ago.
        ProcessTableEntry main = Process(200, 9000, AgentPath, Now.AddDays(-1));
        ProcessTableEntry exec = Process(400, 1, BattleNetPath, Now.AddSeconds(-12));
        ProcessTableEntry fresh = Process(401, 400, AgentPath, Now.AddSeconds(-10));

        BattleNetAgentPlan plan = Select(main, exec, fresh);

        Assert.Same(main, plan.Kept);
        Assert.Empty(plan.Reap);
    }

    [Fact]
    public void Select_ReapsAnOldExtraAgentWhoseParentStillRuns()
    {
        ProcessTableEntry battleNet = Process(100, 1, BattleNetPath, Now.AddDays(-5));
        ProcessTableEntry main = Process(200, 100, AgentPath, Now.AddDays(-5));
        ProcessTableEntry other = Process(500, 1, @"C:\Windows\explorer.exe", Now.AddDays(-5));
        ProcessTableEntry extra = Process(300, 500, AgentPath, Now.AddHours(-1));

        BattleNetAgentPlan plan = Select(battleNet, main, other, extra);

        BattleNetAgentReap reap = Assert.Single(plan.Reap);
        Assert.Same(extra, reap.Process);
        Assert.StartsWith("extra", reap.Reason);
    }

    [Fact]
    public void Select_TreatsAReusedParentPidAsGone()
    {
        // Pid 100 is now a Battle.net.exe started after the agent, so it is not its parent.
        ProcessTableEntry reused = Process(100, 1, BattleNetPath, Now.AddMinutes(-10));
        ProcessTableEntry oldest = Process(200, 9000, AgentPath, Now.AddDays(-2));
        ProcessTableEntry agent = Process(300, 100, AgentPath, Now.AddHours(-1));

        BattleNetAgentPlan plan = Select(reused, oldest, agent);

        Assert.Same(oldest, plan.Kept);
        BattleNetAgentReap reap = Assert.Single(plan.Reap);
        Assert.Same(agent, reap.Process);
        Assert.StartsWith("orphan", reap.Reason);
    }

    [Fact]
    public void Select_NeverSelectsAnythingButBattleNetAgentsAndTheirConsoleHosts()
    {
        DateTimeOffset old = Now.AddDays(-3);
        ProcessTableEntry[] others =
        {
            Process(100, 9000, BattleNetPath, old),
            Process(
                101,
                9000,
                @"C:\Program Files (x86)\Heroes of the Storm\Versions\Base98348\HeroesOfTheStorm_x64.exe",
                old
            ),
            Process(
                102,
                9000,
                @"C:\Program Files (x86)\Heroes of the Storm\Support64\HeroesSwitcher_x64.exe",
                old
            ),
            Process(103, 9000, @"C:\Program Files\obs-studio\bin\64bit\obs64.exe", old),
            Process(104, 9000, @"C:\heroesreplay\app\heroesreplay.exe", old),
            Process(105, 9000, @"C:\Other\Agent.exe", old),
            Process(106, 9000, @"C:\ProgramData\Battle.net\Agent\Agent.9824\NotAgent.exe", old),
            Process(107, 9000, @"C:\Windows\System32\conhost.exe", old),
            new ProcessTableEntry(108, 9000, "Agent.exe", null, null),
        };
        ProcessTableEntry main = Process(200, 9000, AgentPath, Now.AddDays(-4));
        ProcessTableEntry orphan = Process(300, 9001, AgentPath, Now.AddHours(-1));

        BattleNetAgentPlan plan = Select(others.Append(main).Append(orphan).ToArray());

        Assert.Equal(2, plan.AgentCount);
        Assert.Equal(new[] { 300 }, Pids(plan));
    }

    [Fact]
    public void Select_ReapsTheConsoleHostOfAReapedAgentOnly()
    {
        ProcessTableEntry main = Process(200, 9000, AgentPath, Now.AddDays(-4));
        ProcessTableEntry mainHost = Process(
            201,
            200,
            @"C:\Windows\System32\conhost.exe",
            Now.AddDays(-4)
        );
        ProcessTableEntry orphan = Process(300, 9001, AgentPath, Now.AddHours(-1));
        ProcessTableEntry orphanHost = Process(
            301,
            300,
            @"C:\Windows\System32\conhost.exe",
            Now.AddHours(-1)
        );
        ProcessTableEntry olderHost = Process(
            302,
            300,
            @"C:\Windows\System32\conhost.exe",
            Now.AddDays(-9)
        );

        BattleNetAgentPlan plan = Select(main, mainHost, orphan, orphanHost, olderHost);

        Assert.Equal(new[] { 300, 301 }, Pids(plan));
        Assert.Contains("console host", plan.Reap[1].Reason);
    }

    [Fact]
    public void Select_KeepsASingleAgentAndReturnsNothingWithoutAgents()
    {
        ProcessTableEntry only = Process(200, 9000, AgentPath, Now.AddDays(-4));

        Assert.Empty(Select(only).Reap);
        Assert.Same(only, Select(only).Kept);
        Assert.Same(
            BattleNetAgentPlan.None,
            Select(Process(100, 1, BattleNetPath, Now.AddDays(-1)))
        );
    }

    [Fact]
    public void IsAgent_MatchesOnlyAgentExeUnderTheAgentRoot()
    {
        Assert.True(BattleNetAgentSelection.IsAgent(Process(1, 0, AgentPath, Now), Root));
        Assert.True(
            BattleNetAgentSelection.IsAgent(
                Process(1, 0, @"c:\programdata\battle.net\agent\AGENT.EXE", Now),
                Root
            )
        );
        Assert.False(
            BattleNetAgentSelection.IsAgent(
                Process(1, 0, @"C:\ProgramData\Battle.net\AgentX\Agent.exe", Now),
                Root
            )
        );
        Assert.False(
            BattleNetAgentSelection.IsAgent(
                Process(1, 0, @"C:\ProgramData\Battle.net\Agent\..\Agent.exe", Now),
                Root
            )
        );
    }

    private static BattleNetAgentPlan Select(params ProcessTableEntry[] processes) =>
        BattleNetAgentSelection.Select(processes, Root, Now, Grace);

    private static int[] Pids(BattleNetAgentPlan plan) =>
        plan.Reap.Select(item => item.Process.Pid).ToArray();

    private static ProcessTableEntry Process(
        int pid,
        int parent,
        string path,
        DateTimeOffset started
    ) => new(pid, parent, System.IO.Path.GetFileName(path), path, started);
}
