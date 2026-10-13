using System;
using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientHoldTests
{
    [Fact]
    public void LeavesClientOpen_OnlyWhileADialogIsUp()
    {
        Assert.True(ClientHold.LeavesClientOpen(ClientHoldReason.RegionUnavailable));
        Assert.True(ClientHold.LeavesClientOpen(ClientHoldReason.VersionMismatch));
        Assert.True(ClientHold.LeavesClientOpen(ClientHoldReason.BuildNotInstalled));
        Assert.True(ClientHold.LeavesClientOpen(ClientHoldReason.ClientNotReady));
        Assert.False(ClientHold.LeavesClientOpen(ClientHoldReason.None));
        Assert.False(ClientHold.LeavesClientOpen(ClientHoldReason.AwardScreen));
    }

    [Fact]
    public void RetryAfter_WaitsTwoMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), ClientHold.RetryAfter);
    }
}
