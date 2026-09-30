using System;
using System.IO;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayLaunchPlanTests
{
    [Fact]
    public void Decide_LoginOrSplashWaitsWithoutClosing()
    {
        Assert.Equal(
            ReplayLaunchStep.Wait,
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
    public void MatchingExe_StartsThatVersionWithTheReplayPath()
    {
        string exe =
            @"C:\Program Files (x86)\Heroes of the Storm\Versions\Base98025\HeroesOfTheStorm_x64.exe";
        string replay = @"C:\heroesreplay\Data\Requests\65389750 Volskaya Foundry.StormReplay";

        ReplayStartCommand command = ReplayStartCommand.MatchingExe(exe, replay);

        Assert.Equal(exe, command.FileName);
        Assert.Equal("\"" + replay + "\"", command.Arguments);
        Assert.Equal(
            @"C:\Program Files (x86)\Heroes of the Storm\Versions\Base98025",
            command.WorkingDirectory
        );
        Assert.DoesNotContain(
            "HeroesSwitcher",
            command.FileName,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain("Battle.net", command.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cmd.exe", command.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("launch Hero", command.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void FindExe_ReturnsTheInstalledExeForThatBuild()
    {
        var clients = new[]
        {
            new InstalledClient(
                "2.57.0.98304",
                @"C:\Games\Versions\Base98304\HeroesOfTheStorm_x64.exe"
            ),
            new InstalledClient(
                "2.55.17.98025",
                @"C:\Games\Versions\Base98025\HeroesOfTheStorm_x64.exe"
            ),
        };

        Assert.Equal(
            @"C:\Games\Versions\Base98025\HeroesOfTheStorm_x64.exe",
            InstalledClientCatalog.FindExe(clients, "2, 55, 17, 98025")
        );
        Assert.Null(InstalledClientCatalog.FindExe(clients, "2.57.0.98285"));
        Assert.Null(InstalledClientCatalog.FindExe(null, "2.55.17.98025"));
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
