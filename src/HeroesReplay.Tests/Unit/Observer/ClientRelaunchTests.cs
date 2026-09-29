using System;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

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
    public void IsBlankClientWindow_RequiresAnEmptyFullSizeCapture()
    {
        Assert.True(ClientRelaunch.IsBlankClientWindow("", 1280, 720));
        Assert.True(ClientRelaunch.IsBlankClientWindow("   ", 1280, 720));
        Assert.False(ClientRelaunch.IsBlankClientWindow("PLAY", 1280, 720));
        Assert.False(ClientRelaunch.IsBlankClientWindow("", 403, 139));
        Assert.False(ClientRelaunch.IsBlankClientWindow(null, 999, 720));
        Assert.False(ClientRelaunch.IsBlankClientWindow("Preparing game data", 1280, 720));
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
                sawStartup: false
            )
        );
        Assert.Equal(
            ClientHoldReason.ClientNotReady,
            ClientRelaunch.ColdBootHold(
                openedFromHome: false,
                replayFileOpened: true,
                sawStartup: true
            )
        );
        Assert.Equal(
            ClientHoldReason.None,
            ClientRelaunch.ColdBootHold(
                openedFromHome: true,
                replayFileOpened: true,
                sawStartup: true
            )
        );
        Assert.Equal(
            ClientHoldReason.None,
            ClientRelaunch.ColdBootHold(
                openedFromHome: false,
                replayFileOpened: true,
                sawStartup: false
            )
        );
    }

    [Fact]
    public void ShouldRelaunchBlankWindow_OnceTheFullSizeWindowStaysBlank()
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
        Assert.True(
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
}
