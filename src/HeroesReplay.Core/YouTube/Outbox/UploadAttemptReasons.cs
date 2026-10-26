using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

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

    /// <summary>
    /// The open (uploading or ambiguous) attempt of this recording, else a new id. An attempt of
    /// another recording of the same replay, the full match or another clip, is never reused (#524).
    /// </summary>
    public static string SelectContext(
        IReadOnlyList<UploadAttemptManifest> open,
        int? replayId,
        string mediaPath,
        DateTimeOffset utc
    )
    {
        if (replayId is > 0 && open != null)
        {
            foreach (UploadAttemptManifest manifest in open)
            {
                if (
                    manifest == null
                    || manifest.ReplayId != replayId
                    || !SameMedia(manifest.MediaPath, mediaPath)
                )
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

    /// <summary>
    /// The <paramref name="index"/>th id for <paramref name="baseId"/>'s second (#524): a full match
    /// and its clips are dispatched within milliseconds in a dry run, so the second-only id of the
    /// first is <c>replay-44-20261025202159</c> and the next <c>replay-44-20261025202159-2</c>.
    /// </summary>
    public static string InSameSecond(string baseId, int index) =>
        index <= 1 ? baseId : baseId + "-" + index.ToString(CultureInfo.InvariantCulture);

    /// <summary>Both paths name the same file (full paths, ignoring case). A missing path never matches.</summary>
    public static bool SameMedia(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase
            );
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
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
