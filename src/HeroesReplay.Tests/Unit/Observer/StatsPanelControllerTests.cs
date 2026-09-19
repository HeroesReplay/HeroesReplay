using System;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class StatsPanelControllerTests
{
    [Fact]
    public void TryRequest_AcceptsThenCooldownUntilExpiry()
    {
        var controller = new StatsPanelController(
            new AppSettings
            {
                Spectate = new SpectateSettings
                {
                    StatsPanelShowDuration = TimeSpan.FromSeconds(10),
                    StatsPanelCooldown = TimeSpan.FromMinutes(2),
                },
            }
        );

        StatsPanelTryResult first = controller.TryRequest("alice");
        Assert.Equal(StatsPanelTryStatus.Accepted, first.Status);
        Assert.True(controller.TryConsume(out string user));
        Assert.Equal("alice", user);

        StatsPanelTryResult second = controller.TryRequest("bob");
        Assert.Equal(StatsPanelTryStatus.AlreadyVisible, second.Status);

        controller.MarkHidden();
        StatsPanelTryResult third = controller.TryRequest("bob");
        Assert.Equal(StatsPanelTryStatus.Cooldown, third.Status);
        Assert.True(third.CooldownRemaining > TimeSpan.FromMinutes(1));
    }
}
