using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

/// <summary>
/// The text rules for Battle.net's own errors, on the texts the client shows them with (its
/// Battle.net error table and string table, 2.57.0.98348, 2026-10-08). HeroesClientSDK reads
/// them from the dialog's labels in memory (#292); nothing is OCR'd.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class BattleNetErrorTextTests
{
    internal const string RegionUnavailable =
        "The selected region is currently unavailable. Please try again later or select another region.";
    internal const string VersionMismatch =
        "Game client version mismatch with selected region.  You may be able to continue playing if you exit the client, patch from the launcher, and restart.";
    internal const string ServiceLost = "You were disconnected from Blizzard services.";
    internal const string DisconnectedMessage =
        "Blizzard services may be temporarily unavailable or your internet connection may be down. The game will now try to reconnect.";

    [Fact]
    public void IsRegionUnavailable_NamesBattleNetError169()
    {
        Assert.True(BattleNetErrorText.IsRegionUnavailable("Error " + RegionUnavailable));
        Assert.False(BattleNetErrorText.IsRegionUnavailable(VersionMismatch));
        Assert.False(BattleNetErrorText.IsRegionUnavailable(null));
    }

    [Fact]
    public void IsVersionMismatch_NamesBattleNetError153AndItsTitle()
    {
        Assert.True(BattleNetErrorText.IsVersionMismatch(VersionMismatch));
        Assert.True(BattleNetErrorText.IsVersionMismatch("Version Mismatch"));
        Assert.False(BattleNetErrorText.IsVersionMismatch(RegionUnavailable));
        Assert.False(
            BattleNetErrorText.IsVersionMismatch(
                "The version of Heroes of the Storm required to play this game is not available."
            )
        );
        Assert.False(BattleNetErrorText.IsVersionMismatch(""));
    }

    [Theory]
    [InlineData(ServiceLost)]
    [InlineData("Connection Lost " + DisconnectedMessage)]
    [InlineData("Connection was unexpectedly lost")]
    [InlineData("You were disconnected from the server.")]
    [InlineData("You have been disconnected from Battle.net")]
    [InlineData("Connection to Battle.net has been lost. Reconnect")]
    [InlineData("Battle.net\nUnable to connect")]
    public void IsDisconnect_NamesTheClientsDisconnectTexts(string text)
    {
        Assert.True(BattleNetErrorText.IsDisconnect(text));
    }

    [Theory]
    [InlineData(RegionUnavailable)]
    [InlineData(VersionMismatch)]
    [InlineData("There was an unknown error loggin in.")]
    [InlineData("WELCOME TO THE NEXUS")]
    [InlineData(null)]
    public void IsDisconnect_LeavesOtherTextsAlone(string text)
    {
        Assert.False(BattleNetErrorText.IsDisconnect(text));
    }
}
