using System.Collections.Generic;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayClientRouteTests
{
    private static readonly string[] Installed =
    {
        "2.55.17.97650",
        "2.55.17.97771",
        "2.55.17.98025",
        "2.57.0.98285",
    };

    [Fact]
    public void Classify_NewestInstalledBuildIsTheCurrentPatch()
    {
        Assert.Equal(
            ReplayClientPatch.Current,
            ReplayClientRoute.Classify("2.57.0.98285", Installed)
        );
    }

    [Theory]
    [InlineData("2.55.17.98025")]
    [InlineData("2.55.17.97771")]
    [InlineData("2.55.17.97650")]
    public void Classify_OlderInstalledBuildIsThePreviousPatch(string replayVersion)
    {
        Assert.Equal(
            ReplayClientPatch.Previous,
            ReplayClientRoute.Classify(replayVersion, Installed)
        );
    }

    [Fact]
    public void Classify_AnotherIterationOfTheSamePatchIsNotTheCurrentClient()
    {
        Assert.Equal(
            ReplayClientPatch.NotInstalled,
            ReplayClientRoute.Classify("2.57.0.98285", new[] { "2.57.0.98304" })
        );
        Assert.Equal(
            ReplayClientPatch.Previous,
            ReplayClientRoute.Classify("2.57.0.98285", new[] { "2.57.0.98304", "2.57.0.98285" })
        );
    }

    [Fact]
    public void Classify_MissingBuildDoesNotUseTheCurrentClient()
    {
        Assert.Equal(
            ReplayClientPatch.NotInstalled,
            ReplayClientRoute.Classify("2.57.0.98297", Installed)
        );
    }

    [Fact]
    public void Classify_UnreadInstallKeepsTheCurrentSignIn()
    {
        Assert.Equal(
            ReplayClientPatch.Current,
            ReplayClientRoute.Classify("2.55.17.98025", new List<string>())
        );
        Assert.Equal(ReplayClientPatch.Current, ReplayClientRoute.Classify(null, Installed));
    }

    [Fact]
    public void Classify_NormalizesFileVersionCommas()
    {
        Assert.Equal(
            ReplayClientPatch.Current,
            ReplayClientRoute.Classify("2, 57, 0, 98285", Installed)
        );
    }

    [Fact]
    public void Decide_CurrentPatchSignsInBeforeTheReplayFile()
    {
        Assert.Equal(
            ReplayLaunchAuth.AuthenticateCurrent,
            ReplayClientRoute.Decide(
                ReplayClientPatch.Current,
                RunningClientBuild.None,
                homeScreen: false,
                replayPresented: false
            )
        );
    }

    [Fact]
    public void Decide_PreviousPatchOpensThroughTheSwitcher()
    {
        Assert.Equal(
            ReplayLaunchAuth.OpenInstalledBuild,
            ReplayClientRoute.Decide(
                ReplayClientPatch.Previous,
                RunningClientBuild.None,
                homeScreen: false,
                replayPresented: false
            )
        );
        Assert.Equal(
            ReplayLaunchAuth.OpenInstalledBuild,
            ReplayClientRoute.Decide(
                ReplayClientPatch.Previous,
                RunningClientBuild.Differs,
                homeScreen: true,
                replayPresented: false
            )
        );
    }

    [Fact]
    public void Decide_MatchingHomeScreenOpensTheReplay()
    {
        Assert.Equal(
            ReplayLaunchAuth.OpenFromHome,
            ReplayClientRoute.Decide(
                ReplayClientPatch.Current,
                RunningClientBuild.Matches,
                homeScreen: true,
                replayPresented: false
            )
        );
        Assert.Equal(
            ReplayLaunchAuth.OpenFromHome,
            ReplayClientRoute.Decide(
                ReplayClientPatch.Previous,
                RunningClientBuild.Matches,
                homeScreen: true,
                replayPresented: false
            )
        );
    }

    [Fact]
    public void Decide_MatchingPreviousPatchWithoutHomeOpensThatExe()
    {
        Assert.Equal(
            ReplayLaunchAuth.OpenMatchingBuild,
            ReplayClientRoute.Decide(
                ReplayClientPatch.Previous,
                RunningClientBuild.Matches,
                homeScreen: false,
                replayPresented: false
            )
        );
    }

    [Fact]
    public void Decide_MatchingCurrentPatchWithoutHomeWaitsForTheSignedInMenu()
    {
        Assert.Equal(
            ReplayLaunchAuth.Wait,
            ReplayClientRoute.Decide(
                ReplayClientPatch.Current,
                RunningClientBuild.Matches,
                homeScreen: false,
                replayPresented: false
            )
        );
    }

    [Fact]
    public void Decide_MatchingMatchIsLeftAlone()
    {
        Assert.Equal(
            ReplayLaunchAuth.AlreadyInMatch,
            ReplayClientRoute.Decide(
                ReplayClientPatch.Previous,
                RunningClientBuild.Matches,
                homeScreen: false,
                replayPresented: true
            )
        );
    }

    [Fact]
    public void Decide_UnreadableClientIsNotClosed()
    {
        Assert.Equal(
            ReplayLaunchAuth.Wait,
            ReplayClientRoute.Decide(
                ReplayClientPatch.Previous,
                RunningClientBuild.Unreadable,
                homeScreen: false,
                replayPresented: false
            )
        );
    }

    [Fact]
    public void Decide_MissingBuildLaunchesNothing()
    {
        Assert.Equal(
            ReplayLaunchAuth.Unavailable,
            ReplayClientRoute.Decide(
                ReplayClientPatch.NotInstalled,
                RunningClientBuild.None,
                homeScreen: false,
                replayPresented: false
            )
        );
    }

    [Fact]
    public void OpenMatchingBuildNow_OpensABlankMatchingClientOnce()
    {
        Assert.True(
            ReplayClientRoute.OpenMatchingBuildNow(
                ReplayClientRoute.Decide(
                    ReplayClientPatch.Previous,
                    RunningClientBuild.Matches,
                    homeScreen: false,
                    replayPresented: false
                ),
                alreadyOpened: false,
                windowBlank: ClientRelaunch.IsBlankClientWindow("", 1280, 720),
                gameDataStillStarting: false
            )
        );
    }

    [Fact]
    public void OpenMatchingBuildNow_LeavesTheClientUpWhileGameDataStartupIsStillBlank()
    {
        bool blank = ClientRelaunch.IsBlankClientWindow("", 1280, 720);
        bool stillStarting = ClientRelaunch.KeepsWaitingForGameData(
            startupText: false,
            sawStartup: true,
            windowBlank: blank,
            clientAlreadyRunning: false,
            clientBuildMatches: true
        );

        Assert.True(blank);
        Assert.True(stillStarting);
        Assert.False(
            ReplayClientRoute.OpenMatchingBuildNow(
                ReplayClientRoute.Decide(
                    ReplayClientPatch.Previous,
                    RunningClientBuild.Matches,
                    homeScreen: false,
                    replayPresented: false
                ),
                alreadyOpened: false,
                windowBlank: blank,
                gameDataStillStarting: stillStarting
            )
        );
    }

    [Fact]
    public void OpenMatchingBuildNow_SkipsThePreparingDialog()
    {
        Assert.False(
            ReplayClientRoute.OpenMatchingBuildNow(
                ReplayLaunchAuth.OpenMatchingBuild,
                alreadyOpened: false,
                windowBlank: ClientRelaunch.IsBlankClientWindow("Preparing game data", 403, 139),
                gameDataStillStarting: false
            )
        );
    }

    [Fact]
    public void OpenMatchingBuildNow_DoesNotOpenTwiceOrThroughTheSwitcher()
    {
        Assert.False(
            ReplayClientRoute.OpenMatchingBuildNow(
                ReplayLaunchAuth.OpenMatchingBuild,
                alreadyOpened: true,
                windowBlank: true,
                gameDataStillStarting: false
            )
        );
        Assert.False(
            ReplayClientRoute.OpenMatchingBuildNow(
                ReplayLaunchAuth.OpenInstalledBuild,
                alreadyOpened: false,
                windowBlank: true,
                gameDataStillStarting: false
            )
        );
        Assert.False(
            ReplayClientRoute.OpenMatchingBuildNow(
                ReplayLaunchAuth.Wait,
                alreadyOpened: false,
                windowBlank: true,
                gameDataStillStarting: false
            )
        );
        Assert.False(
            ReplayClientRoute.OpenMatchingBuildNow(
                ReplayLaunchAuth.OpenFromHome,
                alreadyOpened: false,
                windowBlank: true,
                gameDataStillStarting: false
            )
        );
    }

    [Fact]
    public void Recover_CurrentPatchAsksBattleNetOnce()
    {
        Assert.Equal(
            ReplaySignInRecovery.LaunchCurrent,
            ReplayClientRoute.Recover(ReplayClientPatch.Current, attemptsAlready: 0)
        );
        Assert.Equal(
            ReplaySignInRecovery.Leave,
            ReplayClientRoute.Recover(ReplayClientPatch.Current, attemptsAlready: 1)
        );
    }

    [Fact]
    public void Recover_PreviousPatchDoesNotLaunchHero()
    {
        Assert.Equal(
            ReplaySignInRecovery.OpenPreviousBuild,
            ReplayClientRoute.Recover(ReplayClientPatch.Previous, attemptsAlready: 0)
        );
        Assert.Equal(
            ReplaySignInRecovery.Leave,
            ReplayClientRoute.Recover(ReplayClientPatch.Previous, attemptsAlready: 1)
        );
    }
}
