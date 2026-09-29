using System;

namespace HeroesReplay.Core.Services.YouTube.Outbox;

/// <summary>Stable result codes. Persisted decisions use these values, not display text.</summary>
public static class UploadAttemptReasons
{
    public const string Ok = "ok";
    public const string ClockNotUtc = "clock-not-utc";
    public const string AttemptIdRequired = "attempt-id-required";
    public const string AttemptIdInvalid = "attempt-id-invalid";
    public const string IllegalTransition = "illegal-transition";
    public const string MediaIncomplete = "media-incomplete";
    public const string YoutubeDisabled = "youtube-disabled";
    public const string DryRun = "dry-run";
    public const string VideoIdRequired = "video-id-required";
    public const string VideoIdConflict = "video-id-conflict";
    public const string AlreadyUploaded = "already-uploaded";
    public const string OperatorRetryRequired = "operator-retry-required";
    public const string ManifestConflict = "manifest-conflict";
    public const string ManifestCorrupt = "manifest-corrupt";
    public const string ManifestMissing = "manifest-missing";
    public const string ManifestBusy = "manifest-busy";
    public const string ManifestUnreadable = "manifest-unreadable";
}

public static class UploadAttemptReceiptKind
{
    public const string Production = "production";
    public const string Simulation = "simulation";
}

public static class UploadAttemptIds
{
    public const int MaxLength = 80;

    public static bool IsSafe(string attemptId)
    {
        if (string.IsNullOrWhiteSpace(attemptId) || attemptId.Length > MaxLength)
        {
            return false;
        }

        foreach (char character in attemptId)
        {
            bool allowed =
                (character >= 'a' && character <= 'z')
                || (character >= 'A' && character <= 'Z')
                || (character >= '0' && character <= '9')
                || character == '-'
                || character == '_';
            if (!allowed)
            {
                return false;
            }
        }

        return !IsReservedDeviceName(attemptId);
    }

    private static bool IsReservedDeviceName(string attemptId)
    {
        string upper = attemptId.ToUpperInvariant();
        if (upper == "CON" || upper == "PRN" || upper == "AUX" || upper == "NUL")
        {
            return true;
        }

        if (upper.Length != 4)
        {
            return false;
        }

        if (
            !upper.StartsWith("COM", StringComparison.Ordinal)
            && !upper.StartsWith("LPT", StringComparison.Ordinal)
        )
        {
            return false;
        }

        char last = upper[3];
        return last >= '1' && last <= '9';
    }
}
