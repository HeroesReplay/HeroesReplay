using System;
using System.Collections.Generic;
using HeroesClientSDK;

namespace HeroesReplay.Core.GameClient;

/// <summary>
/// A game-launch failure read from client memory (#292): the client's game-launch manager holds a
/// failure result and shows it in a <c>CStandardDialog</c> (HeroesClientSDK
/// <see cref="ClientScreenKind.Dialog"/>, <see cref="ClientScreenSample.LaunchResultCode"/> and
/// <see cref="ClientScreenSample.LaunchResult"/>). Any failure result is an invalid client: it is
/// handled exactly like the version dialog (<see cref="ClientHoldReason.VersionMismatch"/>), so the
/// launch stops and the replay is deferred, and while Blizzard should be downloading the build it
/// fails that download. The result keys are the client's own <c>@UI/GameLaunch*</c> table, so no
/// dialog text is read. "The version of Heroes of the Storm required to play this game is not
/// available." is <c>GameLaunchUnsupportedNoData</c> (23), read live on 2.57.0.98348 with a
/// 2.57.0.98297 replay on 2026-10-08.
/// </summary>
public static class ClientLaunchFailure
{
    /// <summary>The dialog the client shows a game-launch result in.</summary>
    public const string ResultDialog = "CStandardDialog";

    /// <summary>
    /// The game-launch results whose message is about a region. Read from the running
    /// 2.57.0.98348 client's own string table on 2026-10-08 (#292): only
    /// <c>GameLaunchUnsupportedInCN</c> (20), "Replays and saved games created before version 1.3.0
    /// are not supported in this region." The generic rule handles it like every other failure.
    /// Battle.net's "The selected region is currently unavailable." is not a game-launch result
    /// (see <see cref="ClientHold.Classify"/>).
    /// </summary>
    public static readonly IReadOnlyList<string> RegionResults = new[]
    {
        "GameLaunchUnsupportedInCN",
    };

    /// <summary>
    /// Results in the game-launch table that are not failures: <c>GameLaunchVersionDownloadMessage</c>
    /// (12) is "All data files must be fully downloaded to load this version of the game.", the
    /// DOWNLOADING message (2.57.0.98348 string table, 2026-10-08).
    /// </summary>
    public static readonly IReadOnlyList<string> NotFailures = new[]
    {
        "GameLaunchVersionDownloadMessage",
    };

    /// <summary>
    /// The failure on screen, or null when memory shows none: no message dialog, no failure
    /// result, or memory cannot tell.
    /// </summary>
    public static LaunchFailure? Read(ClientScreenSample? sample)
    {
        if (
            sample is not ClientScreenSample read
            || !read.Ok
            || read.Screen != ClientScreenKind.Dialog
            || !read.DialogShown(ResultDialog)
            || read.LaunchResultCode is not int code
            || code <= 0
            || string.IsNullOrWhiteSpace(read.LaunchResult)
            || Contains(NotFailures, read.LaunchResult)
        )
        {
            return null;
        }

        return new LaunchFailure(code, read.LaunchResult);
    }

    /// <summary>
    /// The hold for the failure on screen: <see cref="ClientHoldReason.VersionMismatch"/> for any
    /// failure (the invalid-client rule), <see cref="ClientHoldReason.None"/> when there is none.
    /// </summary>
    public static ClientHoldReason Classify(ClientScreenSample? sample) =>
        Read(sample) is null ? ClientHoldReason.None : ClientHoldReason.VersionMismatch;

    internal static bool Contains(IReadOnlyList<string> keys, string key)
    {
        foreach (string candidate in keys)
        {
            if (string.Equals(candidate, key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>A game-launch failure: the client's result code and its message key.</summary>
public readonly record struct LaunchFailure(int Code, string Key)
{
    public bool IsRegion => ClientLaunchFailure.Contains(ClientLaunchFailure.RegionResults, Key);

    public override string ToString() => $"{Code} {Key}";
}
