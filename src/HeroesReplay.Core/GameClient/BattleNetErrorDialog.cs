using System;
using HeroesClientSDK;

namespace HeroesReplay.Core.GameClient;

/// <summary>What a Battle.net error dialog on the client means for the launch.</summary>
public enum BattleNetErrorKind
{
    /// <summary>No Battle.net error is shown, or memory cannot tell.</summary>
    None,

    /// <summary>A disconnect: retry Battle.net once.</summary>
    Disconnect,

    /// <summary>"The selected region is currently unavailable.": hold the replay at the front.</summary>
    RegionUnavailable,

    /// <summary>"Game client version mismatch with selected region.": an invalid client.</summary>
    VersionMismatch,

    /// <summary>
    /// A Battle.net error dialog whose text does not read or names no known error. It takes the
    /// disconnect path (retry Battle.net once); still there after that retry, the client is held
    /// as an invalid client.
    /// </summary>
    Unknown,
}

/// <summary>A Battle.net error dialog read from client memory, and its kind.</summary>
/// <param name="Kind">What it means for the launch.</param>
/// <param name="Dialog">The dialog and its text, or null when none is shown.</param>
public readonly record struct BattleNetErrorRead(BattleNetErrorKind Kind, DialogMessage? Dialog)
{
    public static BattleNetErrorRead None { get; } = new(BattleNetErrorKind.None, null);

    /// <summary>A disconnect or an unknown Battle.net error: close the client, ask Battle.net again once.</summary>
    public bool RetriesBattleNet =>
        Kind is BattleNetErrorKind.Disconnect or BattleNetErrorKind.Unknown;

    /// <summary>The hold for a region or region-version error; None for the others.</summary>
    public ClientHoldReason Hold =>
        Kind switch
        {
            BattleNetErrorKind.RegionUnavailable => ClientHoldReason.RegionUnavailable,
            BattleNetErrorKind.VersionMismatch => ClientHoldReason.VersionMismatch,
            _ => ClientHoldReason.None,
        };

    /// <summary>
    /// The hold when the same error is still shown after the Battle.net retry: an unknown error
    /// is an invalid client (<see cref="ClientHoldReason.VersionMismatch"/>, handled like the
    /// version dialog). A known disconnect keeps the retry rule; the others are held anyway.
    /// </summary>
    public ClientHoldReason HoldAfterRetry =>
        Kind == BattleNetErrorKind.Unknown ? ClientHoldReason.VersionMismatch : Hold;

    public override string ToString() =>
        Dialog is DialogMessage dialog
            ? $"{Kind}, {dialog.Dialog} \"{(dialog.HasText ? dialog.Text : "(text not read)")}\""
            : Kind.ToString();
}

/// <summary>
/// Battle.net's own error dialogs, from client memory (#292), not from the screen. HeroesClientSDK
/// reads a shown <c>CBattlenetErrorDialog</c> or <c>CDisconnectedDialog</c> with the text its
/// labels hold (<c>ClientScreenSample.BattlenetError</c>), and every other shown dialog's text
/// (<c>DialogMessages</c>). The <see cref="BattleNetErrorText"/> rules name the error:
/// region unavailable, then the region version mismatch, then a disconnect (the disconnect dialog
/// is one by its class). A Battle.net error dialog whose text names none of them is
/// <see cref="BattleNetErrorKind.Unknown"/>. Another dialog counts only when its text is a region
/// error. None of these dialogs has been reproduced on ASA-SERVER; their structure and texts come
/// from the client's code and tables.
/// </summary>
public static class BattleNetErrorDialog
{
    /// <summary>The client's disconnect dialog ("Connection Lost").</summary>
    public const string DisconnectedDialog = "CDisconnectedDialog";

    public static BattleNetErrorRead Read(ClientScreenSample? sample)
    {
        if (sample is not ClientScreenSample read)
        {
            return BattleNetErrorRead.None;
        }

        if (read.BattlenetError is DialogMessage error)
        {
            string text = error.Text;
            if (BattleNetErrorText.IsRegionUnavailable(text))
            {
                return new BattleNetErrorRead(BattleNetErrorKind.RegionUnavailable, error);
            }

            if (BattleNetErrorText.IsVersionMismatch(text))
            {
                return new BattleNetErrorRead(BattleNetErrorKind.VersionMismatch, error);
            }

            if (
                string.Equals(error.Dialog, DisconnectedDialog, StringComparison.Ordinal)
                || BattleNetErrorText.IsDisconnect(text)
            )
            {
                return new BattleNetErrorRead(BattleNetErrorKind.Disconnect, error);
            }

            return new BattleNetErrorRead(BattleNetErrorKind.Unknown, error);
        }

        // A Battle.net region error shown in another standard dialog.
        foreach (DialogMessage message in read.DialogMessages ?? Array.Empty<DialogMessage>())
        {
            string text = message.Text;
            if (BattleNetErrorText.IsRegionUnavailable(text))
            {
                return new BattleNetErrorRead(BattleNetErrorKind.RegionUnavailable, message);
            }

            if (BattleNetErrorText.IsVersionMismatch(text))
            {
                return new BattleNetErrorRead(BattleNetErrorKind.VersionMismatch, message);
            }
        }

        return BattleNetErrorRead.None;
    }
}
