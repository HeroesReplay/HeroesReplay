using System;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MatchRecordingTests
{
    [Fact]
    public void ShouldStart_WaitsUntilTheMatchIsVisible()
    {
        Assert.False(MatchRecording.ShouldStart(alreadyRecording: false, matchVisible: false));
        Assert.True(MatchRecording.ShouldStart(alreadyRecording: false, matchVisible: true));
        Assert.False(MatchRecording.ShouldStart(alreadyRecording: true, matchVisible: true));
    }

    [Fact]
    public void ShouldPublish_RequiresAMatchClockAndTwoMinutes()
    {
        Assert.False(MatchRecording.ShouldPublish(0, TimeSpan.FromMinutes(10)));
        Assert.False(MatchRecording.ShouldPublish(4, TimeSpan.FromSeconds(90)));
        Assert.True(MatchRecording.ShouldPublish(4, MatchRecording.MinimumLength));
    }

    [Fact]
    public void ShouldStopLoading_EndsASessionThatNeverShowsTheClock()
    {
        Assert.False(MatchRecording.ShouldStopLoading(sawHud: false, TimeSpan.FromMinutes(2)));
        Assert.True(MatchRecording.ShouldStopLoading(sawHud: false, MatchRecording.LoadingLimit));
        Assert.False(MatchRecording.ShouldStopLoading(sawHud: true, TimeSpan.FromMinutes(10)));
    }
}
