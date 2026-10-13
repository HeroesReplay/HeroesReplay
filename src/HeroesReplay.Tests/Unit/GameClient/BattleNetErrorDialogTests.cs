using HeroesClientSDK;
using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

/// <summary>
/// Battle.net's own error dialogs from client memory (#292). The samples are the shapes
/// HeroesClientSDK 0.4.4 reads: a shown <c>CBattlenetErrorDialog</c> or <c>CDisconnectedDialog</c>
/// with its labels' text (<c>BattlenetError</c>) and every shown dialog's text
/// (<c>DialogMessages</c>). No run on ASA-SERVER has shown one, so the texts are the client's own
/// table entries (2.57.0.98348, 2026-10-08).
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class BattleNetErrorDialogTests
{
    private static readonly HeroesClientVersion Current = new(2, 57, 0, 98348);

    private static ClientScreenSample Sample(
        ClientScreenKind screen,
        params DialogMessage[] dialogs
    ) =>
        new(
            screen,
            new[] { "ScreenLoginUnified" },
            MenuSeen: true,
            "screens",
            Current,
            false,
            Dialogs: System.Array.ConvertAll(dialogs, dialog => dialog.Dialog),
            DialogMessages: dialogs
        );

    private static DialogMessage Error(string title, string message) =>
        new("CBattlenetErrorDialog", title, message);

    [Fact]
    public void Read_NoDialogOrNoRead_IsNone()
    {
        BattleNetErrorRead home = BattleNetErrorDialog.Read(Sample(ClientScreenKind.Home));
        BattleNetErrorRead notRead = BattleNetErrorDialog.Read(null);
        BattleNetErrorRead unknown = BattleNetErrorDialog.Read(
            new ClientScreenSample(ClientScreenKind.Unknown, new string[0], false, "no-state")
        );

        Assert.Equal(BattleNetErrorKind.None, home.Kind);
        Assert.Equal(BattleNetErrorKind.None, notRead.Kind);
        Assert.Equal(BattleNetErrorKind.None, unknown.Kind);
        Assert.False(home.RetriesBattleNet);
        Assert.Equal(ClientHoldReason.None, home.Hold);
    }

    [Fact]
    public void Read_RegionUnavailableHoldsTheReplayAtTheFront()
    {
        BattleNetErrorRead read = BattleNetErrorDialog.Read(
            Sample(
                ClientScreenKind.Dialog,
                Error("Error", BattleNetErrorTextTests.RegionUnavailable)
            )
        );

        Assert.Equal(BattleNetErrorKind.RegionUnavailable, read.Kind);
        Assert.Equal(ClientHoldReason.RegionUnavailable, read.Hold);
        Assert.False(read.RetriesBattleNet);
    }

    [Fact]
    public void Read_RegionVersionMismatchIsAnInvalidClient()
    {
        // The NGDP patch-failure handler: Battle.net error 153 under "Version Mismatch".
        BattleNetErrorRead read = BattleNetErrorDialog.Read(
            Sample(
                ClientScreenKind.Dialog,
                Error("Version Mismatch", BattleNetErrorTextTests.VersionMismatch)
            )
        );

        Assert.Equal(BattleNetErrorKind.VersionMismatch, read.Kind);
        Assert.Equal(ClientHoldReason.VersionMismatch, read.Hold);
        Assert.False(read.RetriesBattleNet);
    }

    [Fact]
    public void Read_ADisconnectRetriesBattleNetOnce()
    {
        BattleNetErrorRead serviceLost = BattleNetErrorDialog.Read(
            Sample(ClientScreenKind.Dialog, Error("Error", BattleNetErrorTextTests.ServiceLost))
        );
        // The disconnect dialog is a disconnect by its class, whatever its text says.
        BattleNetErrorRead disconnected = BattleNetErrorDialog.Read(
            Sample(ClientScreenKind.Dialog, new DialogMessage("CDisconnectedDialog", null, null))
        );

        Assert.Equal(BattleNetErrorKind.Disconnect, serviceLost.Kind);
        Assert.Equal(BattleNetErrorKind.Disconnect, disconnected.Kind);
        Assert.True(serviceLost.RetriesBattleNet);
        Assert.Equal(ClientHoldReason.None, serviceLost.Hold);
        Assert.Equal(ClientHoldReason.None, serviceLost.HoldAfterRetry);
    }

    [Fact]
    public void Read_AnUnknownBattleNetError_RetriesOnce_ThenIsAnInvalidClient()
    {
        // A message that names nothing known, and one whose labels did not read.
        BattleNetErrorRead unknownText = BattleNetErrorDialog.Read(
            Sample(ClientScreenKind.Dialog, Error("Error", "There was an unknown error loggin in."))
        );
        BattleNetErrorRead unread = BattleNetErrorDialog.Read(
            Sample(ClientScreenKind.Dialog, Error(null, null))
        );

        Assert.Equal(BattleNetErrorKind.Unknown, unknownText.Kind);
        Assert.Equal(BattleNetErrorKind.Unknown, unread.Kind);
        Assert.True(unknownText.RetriesBattleNet);
        Assert.Equal(ClientHoldReason.None, unknownText.Hold);
        Assert.Equal(ClientHoldReason.VersionMismatch, unknownText.HoldAfterRetry);
        Assert.Equal(ClientHoldReason.VersionMismatch, unread.HoldAfterRetry);
        Assert.Equal("Unknown, CBattlenetErrorDialog \"(text not read)\"", unread.ToString());
    }

    [Fact]
    public void Read_ARegionErrorInAStandardDialogCountsToo()
    {
        BattleNetErrorRead read = BattleNetErrorDialog.Read(
            Sample(
                ClientScreenKind.Dialog,
                new DialogMessage("CStandardDialog", "", BattleNetErrorTextTests.RegionUnavailable)
            )
        );

        Assert.Equal(BattleNetErrorKind.RegionUnavailable, read.Kind);
    }

    [Fact]
    public void Read_OtherDialogsAreNotBattleNetErrors()
    {
        // The game-launch dialog of the 2.57.0.98297 replay (ClientLaunchFailure decides it), the
        // AUTHENTICATION panel and the purchase dialogs that keep their visible bit at home.
        BattleNetErrorRead read = BattleNetErrorDialog.Read(
            Sample(
                ClientScreenKind.Dialog,
                new DialogMessage(
                    "CStandardDialog",
                    "",
                    "The version of Heroes of the Storm required to play this game is not available."
                ),
                new DialogMessage("CLoginDialog", "Authentication", "Connecting..."),
                new DialogMessage("CLootChestPurchaseDialog", "", ""),
                new DialogMessage("CBoostPurchaseDialog", "Boost Purchase", "")
            )
        );

        Assert.Equal(BattleNetErrorKind.None, read.Kind);
        Assert.Equal(ClientHoldReason.None, read.Hold);
    }
}
