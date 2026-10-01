namespace HeroesReplay.Core.Services.Retention;

/// <summary>
/// The spectate loop asks the disk backlog before it launches another replay.
/// Disk pressure never stops spectating, because the Twitch stream must keep playing.
/// A stop decision only skips the recording for that replay. It does not delete recordings.
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
}
