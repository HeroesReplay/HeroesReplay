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
    /// The game-launch results that name the account's region (the client's
    /// <c>@UI/GameLaunch*</c> keys whose message is about a region). Empty until the keys are read
    /// from a running client.
    /// </summary>
    public static readonly IReadOnlyList<string> RegionResults = Array.Empty<string>();

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

    /// <summary>
    /// True when a failure shows a region result, false on any other known screen, null when
    /// memory cannot tell.
    /// </summary>
    public static bool? ShowsRegion(ClientScreenSample? sample) =>
        Known(sample) ? Read(sample) is LaunchFailure failure && failure.IsRegion : null;

    /// <summary>
    /// True when a failure shows a result that is not about the region (the version dialog and
    /// every other launch failure), false on any other known screen, null when memory cannot tell.
    /// </summary>
    public static bool? ShowsVersion(ClientScreenSample? sample) =>
        Known(sample) ? Read(sample) is LaunchFailure failure && !failure.IsRegion : null;

    private static bool Known(ClientScreenSample? sample) => sample?.Ok == true;
}

/// <summary>A game-launch failure: the client's result code and its message key.</summary>
public readonly record struct LaunchFailure(int Code, string Key)
{
    public bool IsRegion => Contains(ClientLaunchFailure.RegionResults, Key);

    public override string ToString() => $"{Code} {Key}";

    private static bool Contains(IReadOnlyList<string> keys, string key)
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
