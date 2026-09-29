using System;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientHoldTests
{
    [Fact]
    public void Classify_NamesTheRegionDialog()
    {
        Assert.Equal(
            ClientHoldReason.RegionUnavailable,
            ClientHold.Classify(
                "ERROR The selected region is currently unavailable. Please try again later or select another region."
            )
        );
    }

    [Fact]
    public void Classify_NamesTheVersionMismatchDialog()
    {
        Assert.Equal(
            ClientHoldReason.VersionMismatch,
            ClientHold.Classify(
                "VERSION MISMATCH Game client version mismatch with selected region. You may be able to continue playing if you exit the client, patch from the launcher, and restart."
            )
        );
    }

    [Fact]
    public void Classify_LeavesAMatchAndTheHomeScreenAlone()
    {
        Assert.Equal(ClientHoldReason.None, ClientHold.Classify("WELCOME TO CURSED HOLLOW"));
        Assert.Equal(ClientHoldReason.None, ClientHold.Classify("PLAY COLLECTION LOOT WATCH"));
        Assert.Equal(ClientHoldReason.None, ClientHold.Classify(null));
        Assert.Equal(ClientHoldReason.None, ClientHold.Classify(""));
    }

    [Fact]
    public void LeavesClientOpen_OnlyWhileADialogIsUp()
    {
        Assert.True(ClientHold.LeavesClientOpen(ClientHoldReason.RegionUnavailable));
        Assert.True(ClientHold.LeavesClientOpen(ClientHoldReason.VersionMismatch));
        Assert.True(ClientHold.LeavesClientOpen(ClientHoldReason.BuildNotInstalled));
        Assert.False(ClientHold.LeavesClientOpen(ClientHoldReason.None));
    }

    [Fact]
    public void RetryAfter_WaitsTwoMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), ClientHold.RetryAfter);
    }
}
