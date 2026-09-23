using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Twitch.ChatMessages;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class QueueChatCommandTests
{
    [Theory]
    [InlineData("!requests", QueueChatAction.Count, 0)]
    [InlineData("!Requests me", QueueChatAction.Mine, 0)]
    [InlineData("!requests 12", QueueChatAction.At, 12)]
    [InlineData("!queue remove", QueueChatAction.Remove, 0)]
    public void TryRead_MatchesTheCommandPanel(string text, QueueChatAction action, int position)
    {
        Assert.True(QueueChatCommand.TryRead(text, out QueueChatAction read, out int index));
        Assert.Equal(action, read);
        Assert.Equal(position, index);
    }

    [Fact]
    public void MapAndRank_OmitsAnEmptyRank()
    {
        Assert.Equal("Volskaya Foundry", ReplayLabel.MapAndRank("Volskaya Foundry", ""));
        Assert.Equal(
            "Volskaya Foundry (Diamond)",
            ReplayLabel.MapAndRank("Volskaya Foundry", "Diamond")
        );
    }
}
