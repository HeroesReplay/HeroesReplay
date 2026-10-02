using HeroesReplay.CLI.Commands.Twitch.Commands;
using HeroesReplay.Core.Twitch;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ConnectCommandTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ShouldSyncRewards_OnlyWhenRedemptionsAreHandled(
        bool pubSub,
        bool requests,
        bool expected
    )
    {
        var twitch = new TwitchSettings { EnablePubSub = pubSub, EnableRequests = requests };

        Assert.Equal(expected, ConnectCommand.ShouldSyncRewards(twitch));
    }

    [Fact]
    public void ShouldSyncRewards_NoSettings_IsFalse()
    {
        Assert.False(ConnectCommand.ShouldSyncRewards(null));
    }
}
