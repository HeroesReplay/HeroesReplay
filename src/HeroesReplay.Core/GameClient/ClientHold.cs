using System;

namespace HeroesReplay.Core.GameClient;

public enum ClientHoldReason
{
    None,
    VersionMismatch,
    RegionUnavailable,
    BuildNotInstalled,

    /// <summary>
    /// The matching client is still starting. The replay was not opened, so the client stays up.
    /// </summary>
    ClientNotReady,

    /// <summary>
    /// The window is the award screen. The match is over, so the launch wait stops.
    /// </summary>
    AwardScreen,
}

/// <summary>
/// A full-window Heroes dialog is not a match. Leave that client open and try the same replay later.
/// </summary>
public static class ClientHold
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(2);

    public static bool LeavesClientOpen(ClientHoldReason reason) =>
        reason == ClientHoldReason.VersionMismatch
        || reason == ClientHoldReason.RegionUnavailable
        || reason == ClientHoldReason.BuildNotInstalled
        || reason == ClientHoldReason.ClientNotReady;
}
