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

        ConnectivityResume.Decision restored = ConnectivityResume.Decide(
            wasOnline: false,
            isOnline: true,
            streamingEnabled: false
        );
        Assert.True(restored.RetryHeroesProfile);
        Assert.False(restored.StartStream);

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
    public void Decide_StartsStreamOnlyWhenStreamingEnabled()
    {
        ConnectivityResume.Decision live = ConnectivityResume.Decide(
            wasOnline: false,
            isOnline: true,
            streamingEnabled: true
        );
        Assert.True(live.RetryHeroesProfile);
        Assert.True(live.StartStream);

        ConnectivityResume.Decision stillOnline = ConnectivityResume.Decide(
            wasOnline: true,
            isOnline: true,
            streamingEnabled: true
        );
        Assert.False(stillOnline.RetryHeroesProfile);
        Assert.False(stillOnline.StartStream);
    }
}
