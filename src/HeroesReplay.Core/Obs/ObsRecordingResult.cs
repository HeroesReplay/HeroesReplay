using System;
using HeroesReplay.Core.Obs.Inspection;

namespace HeroesReplay.Core.Obs;

public enum ObsOutputFailure
{
    None,
    NotRequested,
    AlreadyRecording,
    NotConfirmed,
    NotOwned,
    RequestError,
    Disconnected,
    Timeout,
    SplitFile,

    /// <summary>OBS has another profile or scene collection active. Nothing was started.</summary>
    SelectionMismatch,

    /// <summary>The preflight (<see cref="ObsValidator.StreamBlockers"/>) found OBS cannot stream. Nothing was started.</summary>
    PreflightFailed,
}

/// <summary>
/// Result of StartRecording or StopRecording. Success never means "the call returned".
/// </summary>
public sealed class ObsRecordingResult
{
    public bool Succeeded { get; private init; }
    public bool Owned { get; private init; }
    public bool Finalized { get; private init; }
    public string OutputPath { get; private init; }
    public ObsOutputFailure Failure { get; private init; }
    public string Detail { get; private init; }

    /// <summary>Stable code for a refused start, such as <c>obs.profile_mismatch</c>.</summary>
    public string Reason { get; private init; }

    public static ObsRecordingResult Started() => new() { Succeeded = true, Owned = true };

    public static ObsRecordingResult FinalizedAt(string path) =>
        new()
        {
            Succeeded = true,
            Owned = true,
            Finalized = true,
            OutputPath = path,
        };

    public static ObsRecordingResult Failed(
        ObsOutputFailure failure,
        string detail,
        string reason = null
    ) =>
        new()
        {
            Failure = failure,
            Detail = detail,
            Reason = reason,
        };
}

/// <summary>
/// Result of StartStream or StopStream. Success means OBS reported the output state.
/// It does not mean the request returned.
/// </summary>
public sealed class ObsStreamResult
{
    public bool Succeeded { get; private init; }

    /// <summary>True when the confirmed output is active. False when it is confirmed inactive.</summary>
    public bool Active { get; private init; }
    public ObsOutputFailure Failure { get; private init; }
    public string Detail { get; private init; }

    /// <summary>
    /// Stable code when the settings want a stream but it was not started, such as
    /// <c>obs.stream_not_armed</c> or <c>obs.collection_mismatch</c>.
    /// </summary>
    public string Reason { get; private init; }

    public static ObsStreamResult ConfirmedActive() =>
        new()
        {
            Succeeded = true,
            Active = true,
            Detail = "OBS reported the stream active.",
        };

    public static ObsStreamResult ConfirmedInactive() =>
        new()
        {
            Succeeded = true,
            Active = false,
            Detail = "OBS reported the stream inactive.",
        };

    public static ObsStreamResult Success() => ConfirmedActive();

    public static ObsStreamResult NotRequested(string detail = null) =>
        new()
        {
            Failure = ObsOutputFailure.NotRequested,
            Detail = string.IsNullOrWhiteSpace(detail) ? "OBS streaming is disabled." : detail,
        };

    /// <summary>
    /// The settings want a stream, but this machine is not armed. Like a disabled stream,
    /// this is not retried and OBS is not contacted.
    /// </summary>
    public static ObsStreamResult NotArmed() =>
        new()
        {
            Failure = ObsOutputFailure.NotRequested,
            Detail = TwitchIngestGuard.NotArmedMessage,
            Reason = ObsStreamArm.NotArmedReason,
        };

    public static ObsStreamResult Failed(
        ObsOutputFailure failure,
        string detail,
        string reason = null
    ) =>
        new()
        {
            Failure = failure,
            Detail = detail,
            Reason = reason,
        };
}

internal sealed class ObsRecordingBudget
{
    public static ObsRecordingBudget Default { get; } = new();

    public int RetryCount { get; init; } = 5;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);
}
