using System;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayLoadCueTests
{
    [Fact]
    public void Classify_ProcessAlone_DoesNotSelectTheGameScene()
    {
        NextMatchLaunch launch = ReplayLoadCue.Classify(
            processRunning: true,
            loadingScreen: false,
            hudTimer: null
        );

        Assert.Equal(NextMatchLaunch.ProcessOnly, launch);
        Assert.False(ReplayLoadCue.SelectsGameScene(launch));
        Assert.False(ReplayLoadCue.SelectsWaitingScene(launch));
    }

    [Fact]
    public void Classify_LoadingScreen_SelectsTheGameScene()
    {
        NextMatchLaunch launch = ReplayLoadCue.Classify(
            processRunning: true,
            loadingScreen: true,
            hudTimer: null
        );

        Assert.Equal(NextMatchLaunch.Presented, launch);
        Assert.True(ReplayLoadCue.SelectsGameScene(launch));
        Assert.False(ReplayLoadCue.SelectsWaitingScene(launch));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    [InlineData(-95)]
    public void Classify_HudTimer_SelectsTheGameScene(int seconds)
    {
        NextMatchLaunch launch = ReplayLoadCue.Classify(
            processRunning: true,
            loadingScreen: false,
            hudTimer: TimeSpan.FromSeconds(seconds)
        );

        Assert.Equal(NextMatchLaunch.Presented, launch);
        Assert.True(ReplayLoadCue.SelectsGameScene(launch));
    }

    [Fact]
    public void Classify_NothingRunning_SelectsTheWaitingScene()
    {
        NextMatchLaunch launch = ReplayLoadCue.Classify(
            processRunning: false,
            loadingScreen: false,
            hudTimer: null
        );

        Assert.Equal(NextMatchLaunch.NotStarted, launch);
        Assert.False(ReplayLoadCue.SelectsGameScene(launch));
        Assert.True(ReplayLoadCue.SelectsWaitingScene(launch));
    }

    [Fact]
    public void SeesLoadingScreen_MatchesWelcomeTextOrTheMap()
    {
        Assert.True(
            ReplayLoadCue.SeesLoadingScreen(
                "WELCOME TO BRAXIS HOLDOUT",
                map: "Braxis Holdout",
                mapAlternative: null,
                playerNames: null,
                heroNames: null,
                loadingScreenText: new[] { "WELCOME TO" }
            )
        );
        Assert.True(
            ReplayLoadCue.SeesLoadingScreen(
                "welcome to alterac",
                map: "Alterac Pass",
                mapAlternative: "Alterac",
                playerNames: null,
                heroNames: null,
                loadingScreenText: null
            )
        );
    }

    [Fact]
    public void SeesLoadingScreen_IgnoresTheHomeScreenAndShortTerms()
    {
        Assert.False(
            ReplayLoadCue.SeesLoadingScreen(
                "PLAY COLLECTION LOOT WATCH",
                map: "AI",
                mapAlternative: " ",
                playerNames: new[] { "Li", "  ab " },
                heroNames: new[] { "Ty" },
                loadingScreenText: new[] { "WELCOME TO" }
            )
        );
        Assert.False(
            ReplayLoadCue.SeesLoadingScreen(
                "   ",
                map: "Braxis Holdout",
                mapAlternative: null,
                playerNames: null,
                heroNames: null,
                loadingScreenText: new[] { "WELCOME TO" }
            )
        );
        Assert.False(ReplayLoadCue.SeesLoadingScreen(null, null, null, null, null, null));
    }

    [Fact]
    public void SeesLoadingScreen_MatchesAHeroOrPlayerOnTheLoadingScreen()
    {
        Assert.True(
            ReplayLoadCue.SeesLoadingScreen(
                "Thrall skiya",
                map: null,
                mapAlternative: null,
                playerNames: new[] { "skiya" },
                heroNames: new[] { "Thrall" },
                loadingScreenText: null
            )
        );
    }
}
