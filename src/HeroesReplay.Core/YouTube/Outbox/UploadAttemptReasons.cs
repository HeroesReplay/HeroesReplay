using System;
using System.Collections.Generic;
using System.Globalization;

namespace HeroesReplay.Core.YouTube.Outbox;

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

    public static string NewContext(int? replayId, DateTimeOffset utc)
    {
        if (utc.Offset != TimeSpan.Zero)
        {
            return null;
        }

        string stamp = utc.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        if (replayId is > 0)
        {
            return "replay-" + replayId.Value.ToString(CultureInfo.InvariantCulture) + "-" + stamp;
        }

        return "file-" + stamp;
    }

    public static string SelectContext(
        IReadOnlyList<UploadAttemptManifest> open,
        int? replayId,
        DateTimeOffset utc
    )
    {
        if (replayId is > 0 && open != null)
        {
            foreach (UploadAttemptManifest manifest in open)
            {
                if (manifest == null || manifest.ReplayId != replayId)
                {
                    continue;
                }

                if (
                    manifest.State == UploadAttemptState.Uploading
                    || manifest.State == UploadAttemptState.AmbiguousUpload
                )
                {
                    return manifest.AttemptId;
                }
            }
        }

        return NewContext(replayId, utc);
    }

    public static bool IsSessionUri(string value)
    {
        if (!UploadAttemptReceipt.HasExactText(value) || value.Length > 2048)
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps;
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
