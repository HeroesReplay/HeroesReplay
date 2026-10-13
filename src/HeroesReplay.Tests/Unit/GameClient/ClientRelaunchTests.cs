using System;
using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientRelaunchTests
{
    [Fact]
    public void ShouldRequestLaunch_SendsTheFirstRequestWhenHeroesIsNotRunning()
    {
        Assert.True(
            ClientRelaunch.ShouldRequestLaunch(
                processRunning: false,
                requestsSent: 0,
                sinceLastRequest: TimeSpan.Zero
            )
        );
    }

    [Fact]
    public void ShouldRequestLaunch_DoesNotSendWhileHeroesIsRunning()
    {
        Assert.False(
            ClientRelaunch.ShouldRequestLaunch(
                processRunning: true,
                requestsSent: 0,
                sinceLastRequest: TimeSpan.Zero
            )
        );
    }

    [Fact]
    public void ShouldRequestLaunch_RetriesOnceAfterTheProcessFailsToAppear()
    {
        Assert.False(
            ClientRelaunch.ShouldRequestLaunch(
                processRunning: false,
                requestsSent: 1,
                sinceLastRequest: ClientRelaunch.RetryIfNoProcess - TimeSpan.FromMilliseconds(1)
            )
        );
        Assert.True(
            ClientRelaunch.ShouldRequestLaunch(
                processRunning: false,
                requestsSent: 1,
                sinceLastRequest: ClientRelaunch.RetryIfNoProcess
            )
        );
    }

    [Fact]
    public void ShouldRequestLaunch_StopsAfterTheSecondRequest()
    {
        Assert.False(
            ClientRelaunch.ShouldRequestLaunch(
                processRunning: false,
                requestsSent: ClientRelaunch.MaxLaunchRequests,
                sinceLastRequest: ClientRelaunch.RetryIfNoProcess
            )
        );
    }

    [Fact]
    public void KeepsWaitingForGameData_HoldsTheBlackWindowAfterStartupText()
    {
        Assert.True(
            ClientRelaunch.KeepsWaitingForGameData(
                startupText: true,
                sawStartup: false,
                windowBlank: false,
                clientAlreadyRunning: false
            )
        );
        Assert.True(
            ClientRelaunch.KeepsWaitingForGameData(
                startupText: false,
                sawStartup: true,
                windowBlank: true,
                clientAlreadyRunning: false
            )
        );
        Assert.True(
            ClientRelaunch.KeepsWaitingForGameData(
                startupText: false,
                sawStartup: false,
                windowBlank: true,
                clientAlreadyRunning: true
            )
        );
        Assert.False(
            ClientRelaunch.KeepsWaitingForGameData(
                startupText: false,
                sawStartup: false,
                windowBlank: true,
                clientAlreadyRunning: false
            )
        );
        Assert.False(
            ClientRelaunch.KeepsWaitingForGameData(
                startupText: false,
                sawStartup: true,
                windowBlank: false,
                clientAlreadyRunning: false
            )
        );
    }

    [Fact]
    public void KeepsWaitingForGameData_DoesNotExtendADifferentBuild()
    {
        Assert.False(
            ClientRelaunch.KeepsWaitingForGameData(
                startupText: true,
                sawStartup: false,
                windowBlank: false,
                clientAlreadyRunning: false,
                clientBuildMatches: false
            )
        );
        Assert.False(
            ClientRelaunch.KeepsWaitingForGameData(
                startupText: false,
                sawStartup: true,
                windowBlank: true,
                clientAlreadyRunning: true,
                clientBuildMatches: false
            )
        );
        Assert.True(
            ClientRelaunch.KeepsWaitingForGameData(
                startupText: true,
                sawStartup: false,
                windowBlank: false,
                clientAlreadyRunning: false,
                clientBuildMatches: true
            )
        );
    }

    [Fact]
    public void MatchingOpenLostTheBuild_StartsTheNextReplayWhenTheExeDidNotStay()
    {
        Assert.True(
            ClientRelaunch.MatchingOpenLostTheBuild(
                openedOnMatchingExe: true,
                runningBuildDiffers: true
            )
        );
        Assert.False(
            ClientRelaunch.MatchingOpenLostTheBuild(
                openedOnMatchingExe: true,
                runningBuildDiffers: false
            )
        );
        Assert.False(
            ClientRelaunch.MatchingOpenLostTheBuild(
                openedOnMatchingExe: false,
                runningBuildDiffers: true
            )
        );
        Assert.False(
            ClientRelaunch.MatchingOpenLostTheBuild(
                openedOnMatchingExe: false,
                runningBuildDiffers: false
            )
        );
    }

    [Fact]
    public void MatchingOpenLeftNoProcess_StartsTheNextReplayWhenNothingStayedUp()
    {
        Assert.False(
            ClientRelaunch.MatchingOpenLeftNoProcess(
                openedOnMatchingExe: true,
                processRunning: false,
                sinceOpen: ClientRelaunch.RetryIfNoProcess - TimeSpan.FromMilliseconds(1)
            )
        );
        Assert.True(
            ClientRelaunch.MatchingOpenLeftNoProcess(
                openedOnMatchingExe: true,
                processRunning: false,
                sinceOpen: ClientRelaunch.RetryIfNoProcess
            )
        );
        Assert.False(
            ClientRelaunch.MatchingOpenLeftNoProcess(
                openedOnMatchingExe: true,
                processRunning: true,
                sinceOpen: ClientRelaunch.RetryIfNoProcess
            )
        );
        Assert.False(
            ClientRelaunch.MatchingOpenLeftNoProcess(
                openedOnMatchingExe: false,
                processRunning: false,
                sinceOpen: ClientRelaunch.RetryIfNoProcess
            )
        );
    }

    [Fact]
    public void ExtendForGameDataStartup_SlidesTheDeadlineWithoutPassingTheCap()
    {
        DateTimeOffset started = new(2026, 9, 29, 21, 0, 0, TimeSpan.Zero);
        DateTimeOffset deadline = started.Add(ClientRelaunch.ColdBootLimit);

        Assert.Equal(deadline, ClientRelaunch.ExtendForGameDataStartup(started, deadline, started));

        DateTimeOffset during = started.AddMinutes(3);
        Assert.Equal(
            during.Add(ClientRelaunch.GameDataStartupExtension),
            ClientRelaunch.ExtendForGameDataStartup(started, deadline, during)
        );

        DateTimeOffset nearCap =
            started.Add(ClientRelaunch.GameDataStartupCap) - TimeSpan.FromMinutes(1);
        Assert.Equal(
            started.Add(ClientRelaunch.GameDataStartupCap),
            ClientRelaunch.ExtendForGameDataStartup(started, deadline, nearCap)
        );
    }

    [Fact]
    public void ColdBootHold_LeavesAnUnopenedClientRunning()
    {
        Assert.Equal(
            ClientHoldReason.ClientNotReady,
            ClientRelaunch.ColdBootHold(
                openedFromHome: false,
                replayFileOpened: false,
                sawStartup: false,
                interfaceRestarted: false,
                processRunning: true
            )
        );
        Assert.Equal(
            ClientHoldReason.ClientNotReady,
            ClientRelaunch.ColdBootHold(
                openedFromHome: false,
                replayFileOpened: true,
                sawStartup: true,
                interfaceRestarted: false,
                processRunning: true
            )
        );
        Assert.Equal(
            ClientHoldReason.None,
            ClientRelaunch.ColdBootHold(
                openedFromHome: true,
                replayFileOpened: true,
                sawStartup: true,
                interfaceRestarted: false,
                processRunning: true
            )
        );
        Assert.Equal(
            ClientHoldReason.None,
            ClientRelaunch.ColdBootHold(
                openedFromHome: false,
                replayFileOpened: true,
                sawStartup: false,
                interfaceRestarted: false,
                processRunning: true
            )
        );
        Assert.Equal(
            ClientHoldReason.ClientNotReady,
            ClientRelaunch.ColdBootHold(
                openedFromHome: false,
                replayFileOpened: true,
                sawStartup: false,
                interfaceRestarted: true,
                processRunning: true
            )
        );
        Assert.Equal(
            ClientHoldReason.ClientNotReady,
            ClientRelaunch.ColdBootHold(
                openedFromHome: false,
                replayFileOpened: true,
                sawStartup: false,
                interfaceRestarted: false,
                processRunning: false
            )
        );
        Assert.Equal(
            ClientHoldReason.ClientNotReady,
            ClientRelaunch.ColdBootHold(
                openedFromHome: true,
                replayFileOpened: true,
                sawStartup: false,
                interfaceRestarted: true,
                processRunning: true
            )
        );
        Assert.Equal(
            ClientHoldReason.ClientNotReady,
            ClientRelaunch.ColdBootHold(
                openedFromHome: false,
                replayFileOpened: true,
                sawStartup: false,
                interfaceRestarted: false,
                processRunning: true,
                clientBuildMatches: false
            )
        );
    }

    [Fact]
    public void ShouldCloseSwitcher_OnlyWhenHeroesIsAlreadyGone()
    {
        Assert.True(
            ClientRelaunch.ShouldCloseSwitcher(heroesRunning: false, switcherRunning: true)
        );
        Assert.False(
            ClientRelaunch.ShouldCloseSwitcher(heroesRunning: true, switcherRunning: true)
        );
        Assert.False(
            ClientRelaunch.ShouldCloseSwitcher(heroesRunning: false, switcherRunning: false)
        );
    }

    [Fact]
    public void DeadlineAfterInterfaceRestart_GivesTheNewProcessAFullColdBoot()
    {
        DateTimeOffset now = new(2026, 9, 30, 1, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            now.Add(ClientRelaunch.ColdBootLimit),
            ClientRelaunch.DeadlineAfterInterfaceRestart(now)
        );
    }

    [Fact]
    public void ShouldRelaunchBlankWindow_DoesNotKillTheHandoffBuild()
    {
        Assert.False(
            ClientRelaunch.ShouldRelaunchBlankWindow(
                processRunning: true,
                replayOpened: false,
                windowBlank: true,
                blankFor: ClientRelaunch.BlankWindowLimit,
                blankRelaunches: 0,
                clientBuildMatches: false
            )
        );
    }

    [Fact]
    public void ShouldRelaunchBlankWindow_LeavesAFullSizeBlankWindowRunning()
    {
        Assert.False(
            ClientRelaunch.ShouldRelaunchBlankWindow(
                processRunning: true,
                replayOpened: false,
                windowBlank: true,
                blankFor: ClientRelaunch.BlankWindowLimit - TimeSpan.FromMilliseconds(1),
                blankRelaunches: 0
            )
        );
        Assert.False(
            ClientRelaunch.ShouldRelaunchBlankWindow(
                processRunning: true,
                replayOpened: false,
                windowBlank: true,
                blankFor: ClientRelaunch.BlankWindowLimit,
                blankRelaunches: 0
            )
        );
    }

    [Fact]
    public void KeepsWaitingForSwitcherHandoff_WhileTheNewestExeIsStillUp()
    {
        Assert.True(
            ClientRelaunch.KeepsWaitingForSwitcherHandoff(
                openedThroughSwitcher: true,
                differentBuild: true,
                processRunning: true
            )
        );
        Assert.False(
            ClientRelaunch.KeepsWaitingForSwitcherHandoff(
                openedThroughSwitcher: false,
                differentBuild: true,
                processRunning: true
            )
        );
        Assert.False(
            ClientRelaunch.KeepsWaitingForSwitcherHandoff(
                openedThroughSwitcher: true,
                differentBuild: false,
                processRunning: true
            )
        );
        Assert.False(
            ClientRelaunch.KeepsWaitingForSwitcherHandoff(
                openedThroughSwitcher: true,
                differentBuild: true,
                processRunning: false
            )
        );
    }

    [Fact]
    public void ShouldRelaunchBlankWindow_DoesNotRepeatOrInterruptAnOpenedReplay()
    {
        Assert.False(
            ClientRelaunch.ShouldRelaunchBlankWindow(
                processRunning: true,
                replayOpened: false,
                windowBlank: true,
                blankFor: ClientRelaunch.BlankWindowLimit,
                blankRelaunches: ClientRelaunch.MaxBlankRelaunches
            )
        );
        Assert.False(
            ClientRelaunch.ShouldRelaunchBlankWindow(
                processRunning: true,
                replayOpened: true,
                windowBlank: true,
                blankFor: ClientRelaunch.BlankWindowLimit,
                blankRelaunches: 0
            )
        );
        Assert.False(
            ClientRelaunch.ShouldRelaunchBlankWindow(
                processRunning: false,
                replayOpened: false,
                windowBlank: true,
                blankFor: ClientRelaunch.BlankWindowLimit,
                blankRelaunches: 0
            )
        );
        Assert.False(
            ClientRelaunch.ShouldRelaunchBlankWindow(
                processRunning: true,
                replayOpened: false,
                windowBlank: false,
                blankFor: ClientRelaunch.BlankWindowLimit,
                blankRelaunches: 0
            )
        );
    }

    [Fact]
    public void KeepsWaitingForGameData_StopsExtendingOnceTheBlackWindowHasLastedTheLimit()
    {
        Assert.False(
            ClientRelaunch.KeepsWaitingForGameData(
                startupText: false,
                sawStartup: true,
                windowBlank: true,
                clientAlreadyRunning: false,
                blankFor: ClientRelaunch.BlankWindowLimit
            )
        );
        Assert.False(
            ClientRelaunch.KeepsWaitingForGameData(
                startupText: false,
                sawStartup: false,
                windowBlank: true,
                clientAlreadyRunning: true,
                blankFor: ClientRelaunch.BlankWindowLimit
            )
        );
        Assert.True(
            ClientRelaunch.KeepsWaitingForGameData(
                startupText: true,
                sawStartup: true,
                windowBlank: false,
                clientAlreadyRunning: false,
                blankFor: ClientRelaunch.BlankWindowLimit
            )
        );
    }

    [Fact]
    public void BlankLaunchIsBroken_ClosesAMatchingClientThatStayedBlack()
    {
        Assert.True(
            ClientRelaunch.BlankLaunchIsBroken(
                processRunning: true,
                windowBlank: true,
                startupOrDownloadVisible: false,
                blankFor: ClientRelaunch.BlankWindowLimit,
                clientBuildMatches: true
            )
        );
        Assert.False(
            ClientRelaunch.BlankLaunchIsBroken(
                processRunning: true,
                windowBlank: true,
                startupOrDownloadVisible: false,
                blankFor: ClientRelaunch.BlankWindowLimit - TimeSpan.FromMilliseconds(1),
                clientBuildMatches: true
            )
        );
    }

    [Fact]
    public void BlankLaunchIsBroken_LeavesStartupAndTheHandoffBuildAlone()
    {
        Assert.False(
            ClientRelaunch.BlankLaunchIsBroken(
                processRunning: true,
                windowBlank: true,
                startupOrDownloadVisible: true,
                blankFor: ClientRelaunch.BlankWindowLimit,
                clientBuildMatches: true
            )
        );
        Assert.False(
            ClientRelaunch.BlankLaunchIsBroken(
                processRunning: true,
                windowBlank: true,
                startupOrDownloadVisible: false,
                blankFor: ClientRelaunch.BlankWindowLimit,
                clientBuildMatches: false
            )
        );
        Assert.False(
            ClientRelaunch.BlankLaunchIsBroken(
                processRunning: false,
                windowBlank: true,
                startupOrDownloadVisible: false,
                blankFor: ClientRelaunch.BlankWindowLimit,
                clientBuildMatches: true
            )
        );
    }
}
