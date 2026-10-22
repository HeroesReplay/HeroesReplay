using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Retention;

/// <summary>
/// The terminal mark of a recording that was retired before it was uploaded (#489). The uploader
/// deletes a recording past the publication window (a Terminal slot), and the spectator's
/// pending-bytes gate clears one the uploader would delete (<see cref="StaleOrdinaryRecordings"/>).
/// Both left <c>youtube-entry.json</c> without a video id, so <see cref="MediaRetention"/> kept the
/// folder forever as one that waits for its insert: 140 folders on the stream PC by 2026-10-22.
/// Renaming the entry to <see cref="EntryFileName"/> ends the wait: the folder then ages out like
/// an uploaded one, <c>Retention:VideoKeepDays</c> after the rename.
/// </summary>
public static class RetiredRecordings
{
    public const string EntryFileName = "youtube-entry-expired.json";

    /// <summary>
    /// Renames the entry beside <paramref name="recording"/> to <see cref="EntryFileName"/>.
    /// False when there is none or it could not be renamed (logged); the next retention pass still
    /// removes the folder once nothing in it can be uploaded.
    /// </summary>
    public static bool MarkExpired(string recording, string entryFileName, ILogger logger = null)
    {
        string directory = string.IsNullOrWhiteSpace(recording)
            ? null
            : Path.GetDirectoryName(recording);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(entryFileName))
        {
            return false;
        }

        string entry = Path.Combine(directory, entryFileName);
        try
        {
            if (!File.Exists(entry))
            {
                return false;
            }

            File.Move(entry, Path.Combine(directory, EntryFileName), overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(
                "Could not mark {Entry} expired: {Message} Retention removes the folder once nothing in it can be uploaded.",
                entry,
                e.Message
            );
            return false;
        }
    }
}
