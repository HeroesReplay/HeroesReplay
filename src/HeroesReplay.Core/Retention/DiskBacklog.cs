namespace HeroesReplay.Core.Retention;

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

    /// <summary>
    /// The replay is a viewer request (media policy priority <c>Requested</c>). The pending-bytes
    /// gates hold back ordinary recordings only, so only the free-space gate can stop a request's
    /// recording (#279).
    /// </summary>
    public bool Requested { get; init; }
}

public sealed class DiskBacklogDecision
{
    public DiskPressure Pressure { get; init; }

    public string Reason { get; init; }

    /// <summary>Whether the replay was evaluated as a viewer request.</summary>
    public bool Requested { get; init; }

    /// <summary>
    /// The gate behind <see cref="Reason"/>: <see cref="DiskBacklog.FreeSpaceGate"/>,
    /// <see cref="DiskBacklog.PendingBytesGate"/>, or <see cref="DiskBacklog.CheckGate"/> when
    /// the disk could not be judged. Null within budget.
    /// </summary>
    public string Gate =>
        Reason switch
        {
            DiskBacklog.FreeBytesWarning or DiskBacklog.FreeBytesLow => DiskBacklog.FreeSpaceGate,
            DiskBacklog.PendingBytesWarning or DiskBacklog.PendingBytesHigh =>
                DiskBacklog.PendingBytesGate,
            DiskBacklog.ConfigurationInvalid or DiskBacklog.MalformedInput => DiskBacklog.CheckGate,
            _ => null,
        };
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

    public const string FreeSpaceGate = "free-space";
    public const string PendingBytesGate = "pending-bytes";
    public const string CheckGate = "disk-check";

    public static DiskBacklogDecision Evaluate(DiskBacklogInput input, DiskBacklogSettings settings)
    {
        bool requested = input?.Requested == true;
        if (settings == null || !ThresholdsValid(settings))
        {
            return Stop(ConfigurationInvalid, requested);
        }

        if (input == null || input.FreeBytes < 0 || input.PendingUploadBytes < 0)
        {
            return Stop(MalformedInput, requested);
        }

        bool freeStop =
            settings.StopWhenFreeBytesBelow > 0
            && input.FreeBytes <= settings.StopWhenFreeBytesBelow;
        bool pendingStop =
            settings.StopWhenPendingBytesAtLeast > 0
            && input.PendingUploadBytes >= settings.StopWhenPendingBytesAtLeast;
        if (freeStop)
        {
            return Stop(FreeBytesLow, requested);
        }

        // Most pending bytes are ordinary recordings waiting for a publish time, and many of them
        // are deleted unuploaded once they pass OrdinaryCandidateMaxAge. They hold back another
        // ordinary recording, never a viewer's request while the disk has real room (#279).
        if (pendingStop && !requested)
        {
            return Stop(PendingBytesHigh, requested);
        }

        bool freeWarn =
            settings.WarnWhenFreeBytesBelow > 0
            && input.FreeBytes <= settings.WarnWhenFreeBytesBelow;
        bool pendingWarn =
            settings.WarnWhenPendingBytesAtLeast > 0
            && input.PendingUploadBytes >= settings.WarnWhenPendingBytesAtLeast;
        if (freeWarn)
        {
            return Warn(FreeBytesWarning, requested);
        }

        if (pendingStop)
        {
            return Warn(PendingBytesHigh, requested);
        }

        if (pendingWarn)
        {
            return Warn(PendingBytesWarning, requested);
        }

        return new DiskBacklogDecision
        {
            Pressure = DiskPressure.Normal,
            Reason = WithinBudget,
            Requested = requested,
        };
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

    private static DiskBacklogDecision Warn(string reason, bool requested)
    {
        return new DiskBacklogDecision
        {
            Pressure = DiskPressure.Warning,
            Reason = reason,
            Requested = requested,
        };
    }

    private static DiskBacklogDecision Stop(string reason, bool requested)
    {
        return new DiskBacklogDecision
        {
            Pressure = DiskPressure.SkipRecording,
            Reason = reason,
            Requested = requested,
        };
    }
}
