using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class BattleNetDisconnectTests
{
    [Theory]
    [InlineData("You have been disconnected from Battle.net")]
    [InlineData("Connection to Battle.net has been lost. Reconnect")]
    [InlineData("Battle.net\nUnable to connect")]
    public void IsShown_DetectsTheDisconnectDialog(string text)
    {
        Assert.True(BattleNetDisconnect.IsShown(text));
    }

    [Fact]
    public void IsShown_IgnoresTheLoadingScreen()
    {
        Assert.False(BattleNetDisconnect.IsShown("WELCOME TO THE NEXUS"));
    }
}
