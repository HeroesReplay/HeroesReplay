using HeroesReplay.Core.Services.Connectivity;
using HeroesReplay.Tests;
using Xunit;

namespace HeroesReplay.Tests.Unit.Connectivity;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ConnectivityResumeTests
{
    [Fact]
    public void Decide_OfflineThenOnline_SetsRetryFlagWithoutStartingStream()
    {
        ConnectivityResume.Decision lost = ConnectivityResume.Decide(
            wasOnline: true,
            isOnline: false,
            streamingEnabled: false
        );
        Assert.False(lost.RetryHeroesProfile);
        Assert.False(lost.StartStream);
        Assert.False(lost.StopStream);

        ConnectivityResume.Decision restored = ConnectivityResume.Decide(
            wasOnline: false,
            isOnline: true,
            streamingEnabled: false
        );
        Assert.True(restored.RetryHeroesProfile);
        Assert.False(restored.StartStream);
        Assert.False(restored.StopStream);

        var resume = new HeroesProfileResume();
        if (restored.RetryHeroesProfile)
        {
            resume.Arm();
        }

        Assert.True(resume.IsPending);
        Assert.True(resume.Consume());
        Assert.False(resume.IsPending);
        Assert.False(resume.Consume());
    }

    [Fact]
    public void Decide_NativeReconnectOwnsAShortOutage()
    {
        ConnectivityResume.Decision restored = ConnectivityResume.Decide(
            wasOnline: false,
            isOnline: true,
            streamingEnabled: true
        );
        Assert.True(restored.RetryHeroesProfile);
        Assert.False(restored.StartStream);
        Assert.False(restored.StopStream);

        ConnectivityResume.Decision lost = ConnectivityResume.Decide(
            wasOnline: true,
            isOnline: false,
            streamingEnabled: true
        );
        Assert.False(lost.RetryHeroesProfile);
        Assert.False(lost.StartStream);
        Assert.False(lost.StopStream);

        ConnectivityResume.Decision stillOnline = ConnectivityResume.Decide(
            wasOnline: true,
            isOnline: true,
            streamingEnabled: true
        );
        Assert.False(stillOnline.RetryHeroesProfile);
        Assert.False(stillOnline.StartStream);
        Assert.False(stillOnline.StopStream);
    }

    [Fact]
    public void Decide_WithoutNativeReconnect_ChangesOutputOnlyWhenStreamingEnabled()
    {
        ConnectivityResume.Decision restored = ConnectivityResume.Decide(
            wasOnline: false,
            isOnline: true,
            streamingEnabled: true,
            nativeReconnectOwnsTransient: false
        );
        Assert.True(restored.RetryHeroesProfile);
        Assert.True(restored.StartStream);
        Assert.False(restored.StopStream);

        ConnectivityResume.Decision lost = ConnectivityResume.Decide(
            wasOnline: true,
            isOnline: false,
            streamingEnabled: true,
            nativeReconnectOwnsTransient: false
        );
        Assert.False(lost.StartStream);
        Assert.True(lost.StopStream);

        ConnectivityResume.Decision disabled = ConnectivityResume.Decide(
            wasOnline: true,
            isOnline: false,
            streamingEnabled: false,
            nativeReconnectOwnsTransient: false
        );
        Assert.False(disabled.StartStream);
        Assert.False(disabled.StopStream);
    }
}
