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
