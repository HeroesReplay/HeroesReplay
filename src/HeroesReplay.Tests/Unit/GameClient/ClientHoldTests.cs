using System;
using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClientHoldTests
{
    [Fact]
    public void Classify_NamesBattleNetsRegionDialog()
    {
        // A Battle.net authentication error, not a game-launch result (2.57.0.98348 string
        // table, 2026-10-08), so its words are still read by OCR (#292).
        Assert.Equal(
            ClientHoldReason.RegionUnavailable,
            ClientHold.Classify(
                "ERROR The selected region is currently unavailable. Please try again later or select another region."
            )
        );
    }

    [Fact]
    public void Classify_NamesBattleNetsRegionVersionMismatchDialog()
    {
        Assert.Equal(
            ClientHoldReason.VersionMismatch,
            ClientHold.Classify(
                "VERSION MISMATCH Game client version mismatch with selected region. You may be able to continue playing if you exit the client, patch from the launcher, and restart."
            )
        );
    }

    [Fact]
    public void Classify_LeavesGameLaunchMessagesToMemory()
    {
        // "Not available" is game-launch result 23, read from memory (ClientLaunchFailure).
        Assert.Equal(
            ClientHoldReason.None,
            ClientHold.Classify(
                "The version of Heroes of the Storm required to ploy this game is not available. 0K"
            )
        );
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
