using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace HeroesReplay.Core.Obs.Collection;

/// <summary>
/// Writes a file OBS reads: the live scene collection or a profile. The current file is copied
/// into the backup folder first, the new contents go to a temp file beside it, and the temp file
/// then replaces it in one step. A failure leaves the original as it was and removes the temp file.
/// </summary>
public static class ObsFileTransaction
{
    /// <summary>The newest backups of each file that are kept. Older ones are deleted.</summary>
    public const int BackupsKept = 10;

    private const string BackupExtension = ".bak";

    private const string StampFormat = "yyyyMMdd'T'HHmmssfff'Z'";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Replaces <paramref name="path"/> with <paramref name="contents"/> (UTF-8, no BOM) and returns
    /// the backup of the previous file, or null when there was none.
    /// </summary>
    /// <exception cref="IOException">The backup or the write failed; <paramref name="path"/> is unchanged.</exception>
    /// <exception cref="UnauthorizedAccessException">The same, for a file or folder this user cannot write.</exception>
    public static string Write(
        string path,
        string contents,
        string backupDirectory,
        DateTime utcNow
    ) => Write(path, Utf8.GetBytes(contents ?? string.Empty), backupDirectory, utcNow);

    /// <summary>
    /// Replaces <paramref name="path"/> with exactly <paramref name="contents"/>, with the same
    /// backup and atomic swap. A rollback puts a backup's bytes back unchanged.
    /// </summary>
    /// <exception cref="IOException">The backup or the write failed; <paramref name="path"/> is unchanged.</exception>
    /// <exception cref="UnauthorizedAccessException">The same, for a file or folder this user cannot write.</exception>
    public static string Write(
        string path,
        byte[] contents,
        string backupDirectory,
        DateTime utcNow
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);

        string backup = null;
        if (File.Exists(full))
        {
            backup = Backup(full, backupDirectory, utcNow);
        }

        string temp = Path.Combine(
            directory,
            "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp"
        );
        try
        {
            File.WriteAllBytes(temp, contents ?? []);
            if (File.Exists(full))
            {
                File.Replace(temp, full, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temp, full);
            }
        }
        finally
        {
            TryDelete(temp);
        }

        return backup;
    }

    /// <summary>The backups of <paramref name="path"/>, newest first.</summary>
    public static string[] Backups(string backupDirectory, string path)
    {
        string key = BackupKey(Path.GetFullPath(path));
        if (!Directory.Exists(backupDirectory))
        {
            return [];
        }

        // The UTC stamp sorts in time order.
        return Directory
            .GetFiles(backupDirectory, key + ".*" + BackupExtension)
            .OrderByDescending(file => Path.GetFileName(file), StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The backups of <paramref name="path"/> taken at or after <paramref name="sinceUtc"/>, oldest
    /// first. The first one is the file as it was before the first write from that time on.
    /// </summary>
    public static string[] BackupsSince(string backupDirectory, string path, DateTime sinceUtc)
    {
        DateTime since = WholeMilliseconds(sinceUtc.ToUniversalTime());
        return Backups(backupDirectory, path)
            .Where(file => BackupTime(file) is DateTime taken && taken >= since)
            .Reverse()
            .ToArray();
    }

    /// <summary>When a backup was taken, read from its name. Null for any other file.</summary>
    public static DateTime? BackupTime(string backup)
    {
        string name = Path.GetFileName(backup ?? string.Empty);
        if (!name.EndsWith(BackupExtension, StringComparison.Ordinal))
        {
            return null;
        }

        string stem = name.Substring(0, name.Length - BackupExtension.Length);
        int dot = stem.LastIndexOf('.');
        if (
            dot < 0
            || !DateTime.TryParseExact(
                stem.Substring(dot + 1),
                StampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime taken
            )
        )
        {
            return null;
        }

        return taken;
    }

    private static string Backup(string full, string backupDirectory, DateTime utcNow)
    {
        Directory.CreateDirectory(backupDirectory);
        string backup = Path.Combine(
            backupDirectory,
            BackupKey(full)
                + "."
                + utcNow.ToUniversalTime().ToString(StampFormat, CultureInfo.InvariantCulture)
                + BackupExtension
        );
        File.Copy(full, backup, overwrite: true);
        foreach (string old in Backups(backupDirectory, full).Skip(BackupsKept))
        {
            TryDelete(old);
        }

        return backup;
    }

    /// <summary>A backup's name keeps the time to the millisecond.</summary>
    private static DateTime WholeMilliseconds(DateTime utc) =>
        new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

    /// <summary>
    /// The folder and file name, so the profiles' basic.ini files and the scene collections each
    /// keep their own backups: <c>scenes-HeroesReplay.json</c>, <c>HeroesReplay-basic.ini</c>.
    /// </summary>
    private static string BackupKey(string full) =>
        Path.GetFileName(Path.GetDirectoryName(full)) + "-" + Path.GetFileName(full);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover temp file or an extra backup does not change what OBS loads.
        }
    }
}
