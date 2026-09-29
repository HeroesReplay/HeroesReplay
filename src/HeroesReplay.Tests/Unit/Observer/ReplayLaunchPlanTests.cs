using System;
using System.IO;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayLaunchPlanTests
{
    [Fact]
    public void Decide_LoginWindowClosesAndOpensTheReplayAgain()
    {
        Assert.Equal(
            ReplayLaunchStep.CloseThenOpen,
            ReplayLaunchPlan.Decide(processRunning: true, replayPresented: false, homeScreen: false)
        );
    }

    [Fact]
    public void Decide_HomeScreenOpensTheReplayWithoutClosing()
    {
        Assert.Equal(
            ReplayLaunchStep.Open,
            ReplayLaunchPlan.Decide(processRunning: true, replayPresented: false, homeScreen: true)
        );
    }

    [Fact]
    public void Decide_LoadingScreenOrMatchClockIsLeftAlone()
    {
        Assert.Equal(
            ReplayLaunchStep.AlreadyInMatch,
            ReplayLaunchPlan.Decide(processRunning: true, replayPresented: true, homeScreen: false)
        );
    }

    [Fact]
    public void Decide_WhenTheClientIsNotRunning_OpensTheReplay()
    {
        Assert.Equal(
            ReplayLaunchStep.Open,
            ReplayLaunchPlan.Decide(
                processRunning: false,
                replayPresented: false,
                homeScreen: false
            )
        );
    }

    [Fact]
    public void For_UsesHeroesSwitcherWhenItIsInstalled()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-switch-" + Path.GetRandomFileName());
        string support = Path.Combine(root, "Support64");
        try
        {
            Directory.CreateDirectory(support);
            string switcher = Path.Combine(support, "HeroesSwitcher_x64.exe");
            File.WriteAllText(switcher, "not a real exe");
            string replay = Path.Combine(root, "Braxis Holdout.StormReplay");

            ReplayStartCommand command = ReplayStartCommand.For(root, replay);

            Assert.Equal(switcher, command.FileName);
            Assert.Equal("\"" + replay + "\"", command.Arguments);
            Assert.Equal(support, command.WorkingDirectory);
            Assert.DoesNotContain("cmd.exe", command.FileName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void For_UsesTheShellAssociationWhenSwitcherIsMissing()
    {
        string replay = Path.Combine(Path.GetTempPath(), "match.StormReplay");

        ReplayStartCommand command = ReplayStartCommand.For(Path.GetTempPath(), replay);

        Assert.EndsWith("cmd.exe", command.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/c start \"\"", command.Arguments, StringComparison.Ordinal);
        Assert.Contains("\"" + replay + "\"", command.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void HeroClient_AsksTheLoggedInBattleNetToLaunchHeroes()
    {
        ReplayStartCommand command = ReplayStartCommand.HeroClient(
            @"C:\heroesreplay\Battle.net\Battle.net.exe"
        );

        Assert.Equal(@"C:\heroesreplay\Battle.net\Battle.net.exe", command.FileName);
        Assert.Equal("--exec=\"launch Hero\"", command.Arguments);
        Assert.Equal(@"C:\heroesreplay\Battle.net", command.WorkingDirectory);
    }
}
