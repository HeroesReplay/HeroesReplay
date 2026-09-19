using System;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObserverPanelRequestsTests
{
    [Fact]
    public void TryRequest_StatsAndTalentsHaveIndependentCooldowns()
    {
        var controller = new ObserverPanelRequests(
            new AppSettings
            {
                Spectate = new SpectateSettings
                {
                    StatsPanelShowDuration = TimeSpan.FromSeconds(10),
                    StatsPanelCooldown = TimeSpan.FromMinutes(2),
                },
            }
        );

        Assert.Equal(
            ObserverPanelTryStatus.Accepted,
            controller.TryRequest(Panel.DeathDamageRole, "alice").Status
        );
        Assert.True(controller.TryConsume(out Panel panel, out string user));
        Assert.Equal(Panel.DeathDamageRole, panel);
        Assert.Equal("alice", user);

        Assert.Equal(
            ObserverPanelTryStatus.AlreadyVisible,
            controller.TryRequest(Panel.DeathDamageRole, "bob").Status
        );
        Assert.Equal(
            ObserverPanelTryStatus.Accepted,
            controller.TryRequest(Panel.Talents, "bob").Status
        );

        controller.MarkHidden(Panel.DeathDamageRole);
        Assert.Equal(
            ObserverPanelTryStatus.Cooldown,
            controller.TryRequest(Panel.DeathDamageRole, "bob").Status
        );
    }
}
