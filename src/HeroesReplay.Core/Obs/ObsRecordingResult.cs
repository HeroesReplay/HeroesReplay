using System;

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

    public static ObsRecordingResult Started() => new() { Succeeded = true, Owned = true };

    public static ObsRecordingResult FinalizedAt(string path) =>
        new()
        {
            Succeeded = true,
            Owned = true,
            Finalized = true,
            OutputPath = path,
        };

    public static ObsRecordingResult Failed(ObsOutputFailure failure, string detail) =>
        new() { Failure = failure, Detail = detail };
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

    public static ObsStreamResult Failed(ObsOutputFailure failure, string detail) =>
        new() { Failure = failure, Detail = detail };
}

/// <summary>
/// A publishable file is the path OBS finalized for a recording this process owns.
/// </summary>
public static class RecordingOwnership
{
    public static bool CanPublish(ObsRecordingResult recording, bool allowsMedia) =>
        recording != null
        && recording.Succeeded
        && recording.Owned
        && recording.Finalized
        && allowsMedia
        && !string.IsNullOrWhiteSpace(recording.OutputPath);

    public static string SelectFinalizedFile(string outputPath, string contextDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return null;
        }

        // The context directory is not searched. A newer file there is not this recording.
        if (string.IsNullOrWhiteSpace(contextDirectory))
        {
            return outputPath;
        }

        return outputPath;
    }

    public static string FileToDiscard(ObsRecordingResult recording, bool allowsMedia)
    {
        if (
            recording == null
            || allowsMedia
            || !recording.Owned
            || !recording.Finalized
            || string.IsNullOrWhiteSpace(recording.OutputPath)
        )
        {
            return null;
        }

        return recording.OutputPath;
    }
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
