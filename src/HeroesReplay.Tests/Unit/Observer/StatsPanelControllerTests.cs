using System;
using System.IO;
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
        var controller = new ObserverPanelRequests(Settings(), TempFile());

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

    [Fact]
    public void TwoInstances_ShareTheRequestFile()
    {
        string path = TempFile();
        var twitch = new ObserverPanelRequests(Settings(), path);
        var spectator = new ObserverPanelRequests(Settings(), path);

        Assert.Equal(
            ObserverPanelTryStatus.Accepted,
            twitch.TryRequest(Panel.Talents, "alice").Status
        );
        Assert.True(spectator.TryConsume(out Panel panel, out string user));
        Assert.Equal(Panel.Talents, panel);
        Assert.Equal("alice", user);
        Assert.Equal(
            ObserverPanelTryStatus.AlreadyVisible,
            twitch.TryRequest(Panel.Talents, "bob").Status
        );
    }

    private static AppSettings Settings() =>
        new()
        {
            Spectate = new SpectateSettings
            {
                StatsPanelShowDuration = TimeSpan.FromSeconds(10),
                StatsPanelCooldown = TimeSpan.FromMinutes(2),
            },
        };

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"heroesreplay-panels-{Guid.NewGuid():N}.json");
}
