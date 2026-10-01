namespace HeroesReplay.Core.Services.Retention;

public enum DiskPressure
{
    Normal = 0,
    Warning = 1,
    SkipRecording = 2,
}

/// <summary>
/// Zero means that signal is off. A warning watermark must trip before the stop watermark.
/// </summary>
public sealed class DiskBacklogSettings
{
    public long WarnWhenFreeBytesBelow { get; set; }

    public long StopWhenFreeBytesBelow { get; set; }

    public long WarnWhenPendingBytesAtLeast { get; set; }

    public long StopWhenPendingBytesAtLeast { get; set; }
}

public sealed class DiskBacklogInput
{
    public long FreeBytes { get; init; }

    public long PendingUploadBytes { get; init; }
}

public sealed class DiskBacklogDecision
{
    public DiskPressure Pressure { get; init; }

    public string Reason { get; init; }
}

/// <summary>
/// Decides whether finalized recordings are filling the disk. It never deletes them.
/// </summary>
public static class DiskBacklog
{
    public const string WithinBudget = "within-budget";
    public const string FreeBytesWarning = "free-bytes-warning";
    public const string PendingBytesWarning = "pending-bytes-warning";
    public const string FreeBytesLow = "free-bytes-low";
    public const string PendingBytesHigh = "pending-bytes-high";
    public const string ConfigurationInvalid = "configuration-invalid";
    public const string MalformedInput = "malformed-input";

    public static DiskBacklogDecision Evaluate(DiskBacklogInput input, DiskBacklogSettings settings)
    {
        if (settings == null || !ThresholdsValid(settings))
        {
            return Stop(ConfigurationInvalid);
        }

        if (input == null || input.FreeBytes < 0 || input.PendingUploadBytes < 0)
        {
            return Stop(MalformedInput);
        }

        bool freeStop =
            settings.StopWhenFreeBytesBelow > 0
            && input.FreeBytes <= settings.StopWhenFreeBytesBelow;
        bool pendingStop =
            settings.StopWhenPendingBytesAtLeast > 0
            && input.PendingUploadBytes >= settings.StopWhenPendingBytesAtLeast;
        if (freeStop)
        {
            return Stop(FreeBytesLow);
        }

        if (pendingStop)
        {
            return Stop(PendingBytesHigh);
        }

        bool freeWarn =
            settings.WarnWhenFreeBytesBelow > 0
            && input.FreeBytes <= settings.WarnWhenFreeBytesBelow;
        bool pendingWarn =
            settings.WarnWhenPendingBytesAtLeast > 0
            && input.PendingUploadBytes >= settings.WarnWhenPendingBytesAtLeast;
        if (freeWarn)
        {
            return new DiskBacklogDecision
            {
                Pressure = DiskPressure.Warning,
                Reason = FreeBytesWarning,
            };
        }

        if (pendingWarn)
        {
            return new DiskBacklogDecision
            {
                Pressure = DiskPressure.Warning,
                Reason = PendingBytesWarning,
            };
        }

        return new DiskBacklogDecision { Pressure = DiskPressure.Normal, Reason = WithinBudget };
    }

    private static bool ThresholdsValid(DiskBacklogSettings settings)
    {
        if (
            settings.WarnWhenFreeBytesBelow < 0
            || settings.StopWhenFreeBytesBelow < 0
            || settings.WarnWhenPendingBytesAtLeast < 0
            || settings.StopWhenPendingBytesAtLeast < 0
        )
        {
            return false;
        }

        if (
            settings.WarnWhenFreeBytesBelow > 0
            && settings.StopWhenFreeBytesBelow > 0
            && settings.WarnWhenFreeBytesBelow <= settings.StopWhenFreeBytesBelow
        )
        {
            return false;
        }

        if (
            settings.WarnWhenPendingBytesAtLeast > 0
            && settings.StopWhenPendingBytesAtLeast > 0
            && settings.WarnWhenPendingBytesAtLeast >= settings.StopWhenPendingBytesAtLeast
        )
        {
            return false;
        }

        return true;
    }

    private static DiskBacklogDecision Stop(string reason)
    {
        return new DiskBacklogDecision { Pressure = DiskPressure.SkipRecording, Reason = reason };
    }
}
