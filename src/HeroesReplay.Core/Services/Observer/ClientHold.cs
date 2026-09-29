using System;

namespace HeroesReplay.Core.Services.Observer;

public enum ClientHoldReason
{
    None,
    VersionMismatch,
    RegionUnavailable,
}

/// <summary>
/// A full-window Heroes dialog is not a match. Leave that client open and try the same replay later.
/// </summary>
public static class ClientHold
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(2);

    public static ClientHoldReason Classify(string text)
    {
        if (ClientScreenText.IsRegionUnavailable(text))
        {
            return ClientHoldReason.RegionUnavailable;
        }

        if (ClientScreenText.IsVersionMismatch(text))
        {
            return ClientHoldReason.VersionMismatch;
        }

        return ClientHoldReason.None;
    }

    public static bool LeavesClientOpen(ClientHoldReason reason) => reason != ClientHoldReason.None;
}
