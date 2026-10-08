using System;

namespace HeroesReplay.Core.Retention;

/// <summary>What the disk allows for the replay about to launch.</summary>
public sealed class DiskAdmission
{
    public DiskBacklogDecision Decision { get; init; }

    /// <summary>The measurement behind <see cref="Decision"/>.</summary>
    public DiskBacklogInput Measured { get; init; }

    /// <summary>
    /// Ordinary recordings past <c>ReplayMedia:OrdinaryCandidateMaxAge</c> cleared because the
    /// pending-bytes gate tripped. Null when the gate did not trip.
    /// </summary>
    public RetentionSweep Cleared { get; init; }

    public bool MayRecord => SpectateAdmission.MayRecord(Decision);
}

/// <summary>
/// The spectate loop asks the disk backlog before it launches another replay.
/// Disk pressure never stops spectating, because the Twitch stream must keep playing.
/// A stop decision only skips the recording for that replay. It does not delete recordings
/// that can still be published.
/// </summary>
public static class SpectateAdmission
{
    public const long WarnFreeBytes = 8L * 1024 * 1024 * 1024;
    public const long StopFreeBytes = 4L * 1024 * 1024 * 1024;
    public const long WarnPendingBytes = 8L * 1024 * 1024 * 1024;
    public const long StopPendingBytes = 15L * 1024 * 1024 * 1024;

    public static DiskBacklogSettings DefaultWatermarks()
    {
        return new DiskBacklogSettings
        {
            WarnWhenFreeBytesBelow = WarnFreeBytes,
            StopWhenFreeBytesBelow = StopFreeBytes,
            WarnWhenPendingBytesAtLeast = WarnPendingBytes,
            StopWhenPendingBytesAtLeast = StopPendingBytes,
        };
    }

    public static DiskBacklogDecision Evaluate(DiskBacklogInput input, DiskBacklogSettings settings)
    {
        return DiskBacklog.Evaluate(input, settings ?? DefaultWatermarks());
    }

    public static bool MayRecord(DiskBacklogDecision decision)
    {
        return decision != null && decision.Pressure != DiskPressure.SkipRecording;
    }

    /// <summary>
    /// Measures the disk and decides. When the pending-bytes gate trips, recordings that can
    /// never be published are cleared first (<paramref name="clearStale"/>), and the disk is
    /// measured again before the decision stands (#279). A request is held back only by the
    /// free-space gate.
    /// </summary>
    public static DiskAdmission Admit(
        bool requested,
        DiskBacklogSettings settings,
        Func<DiskBacklogInput> measure,
        Func<RetentionSweep> clearStale = null
    )
    {
        DiskBacklogInput measured = AsRequest(measure?.Invoke(), requested);
        DiskBacklogDecision decision = Evaluate(measured, settings);
        if (decision.Reason != DiskBacklog.PendingBytesHigh || clearStale == null)
        {
            return new DiskAdmission { Decision = decision, Measured = measured };
        }

        RetentionSweep cleared = clearStale() ?? new RetentionSweep();
        if (cleared.DeletedFiles == 0)
        {
            return new DiskAdmission
            {
                Decision = decision,
                Measured = measured,
                Cleared = cleared,
            };
        }

        DiskBacklogInput after = AsRequest(measure(), requested);
        return new DiskAdmission
        {
            Decision = Evaluate(after, settings),
            Measured = after,
            Cleared = cleared,
        };
    }

    private static DiskBacklogInput AsRequest(DiskBacklogInput input, bool requested)
    {
        if (input == null)
        {
            return null;
        }

        return new DiskBacklogInput
        {
            FreeBytes = input.FreeBytes,
            PendingUploadBytes = input.PendingUploadBytes,
            Requested = requested,
        };
    }
}
