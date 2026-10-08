using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace HeroesReplay.Core.Obs.Collection;

/// <summary>One backup of the live collection.</summary>
public sealed record ObsBackupInfo
{
    public string Path { get; init; }

    public DateTime? TakenAtUtc { get; init; }

    public long Bytes { get; init; }

    public string Sha256 { get; init; }
}

/// <summary>The stable <c>code</c> of <c>obs backup</c> and <c>obs restore</c>.</summary>
public static class ObsBackupCodes
{
    /// <summary><c>obs backup</c> copied the live collection into the backups.</summary>
    public const string BackedUp = "obs.backed_up";

    /// <summary><c>obs backup --list</c>.</summary>
    public const string Listed = "obs.backups_listed";

    /// <summary>There is no live collection to back up.</summary>
    public const string CollectionMissing = "obs.collection_missing";

    /// <summary><c>obs restore</c> wrote the backup over the live collection.</summary>
    public const string Restored = "obs.restored";

    /// <summary>The live collection already has the backup's bytes; nothing was written.</summary>
    public const string AlreadyRestored = "obs.already_restored";

    /// <summary>OBS is running: it would overwrite the file. Not ok.</summary>
    public const string ObsRunning = "obs.restore_obs_running";

    /// <summary>The backup does not exist. Not ok.</summary>
    public const string BackupMissing = "obs.backup_missing";

    /// <summary>The file is not a backup of this collection (another collection or a profile). Not ok.</summary>
    public const string BackupOther = "obs.backup_other_file";

    /// <summary>The backup is not a valid collection. Not ok.</summary>
    public const string BackupInvalid = "obs.backup_invalid";

    /// <summary>The backup or the write failed; the collection is as it was. Not ok.</summary>
    public const string Failed = "obs.restore_failed";
}

/// <summary>The <c>obs backup</c> and <c>obs restore</c> result envelope.</summary>
public sealed record ObsBackupResult
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool Ok { get; init; }

    public string Code { get; init; }

    public string Message { get; init; }

    public string Collection { get; init; }

    /// <summary>The backup taken (<c>obs backup</c>) or put back (<c>obs restore</c>).</summary>
    public string Backup { get; init; }

    /// <summary><c>obs restore</c>: where the collection it replaced was saved.</summary>
    public string Saved { get; init; }

    /// <summary>The collection's backups, newest first.</summary>
    public IReadOnlyList<ObsBackupInfo> Backups { get; init; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

/// <summary>
/// <c>obs backup</c> and <c>obs restore</c> (#307): the live collection's backups in
/// <c>%LOCALAPPDATA%\HeroesReplay\obs\backups</c>, the same ones every write takes
/// (<see cref="ObsFileTransaction"/>). A backup only copies the live file. A restore writes a
/// backup's exact bytes back through the same transaction, so the collection it replaces becomes
/// a backup too, and it is refused while OBS runs: OBS saves its collection over the file.
/// </summary>
public static class ObsCollectionBackups
{
    public static ObsBackupResult List(ObsManagedFiles managed, string collectionPath)
    {
        ArgumentNullException.ThrowIfNull(managed);
        IReadOnlyList<ObsBackupInfo> backups = Describe(managed, collectionPath);
        return new ObsBackupResult
        {
            Ok = true,
            Code = ObsBackupCodes.Listed,
            Collection = Path.GetFullPath(collectionPath),
            Backups = backups,
            Message =
                $"{backups.Count} backup(s) of {Path.GetFullPath(collectionPath)} in {managed.BackupDirectory}.",
        };
    }

    /// <summary>Copies the live collection into the backups. Safe while OBS runs: it only reads it.</summary>
    public static ObsBackupResult Take(
        ObsManagedFiles managed,
        string collectionPath,
        DateTime utcNow
    )
    {
        ArgumentNullException.ThrowIfNull(managed);
        string collection = Path.GetFullPath(collectionPath);
        string backup;
        try
        {
            backup = ObsFileTransaction.Snapshot(collection, managed.BackupDirectory, utcNow);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ObsBackupResult
            {
                Ok = false,
                Code = ObsBackupCodes.Failed,
                Collection = collection,
                Message = "The collection was not backed up. " + e.Message,
            };
        }

        return backup == null
            ? new ObsBackupResult
            {
                Ok = false,
                Code = ObsBackupCodes.CollectionMissing,
                Collection = collection,
                Message = "There is no live collection at " + collection + " to back up.",
            }
            : new ObsBackupResult
            {
                Ok = true,
                Code = ObsBackupCodes.BackedUp,
                Collection = collection,
                Backup = backup,
                Backups = Describe(managed, collection),
                Message =
                    $"Backed up {collection} to {backup}. The newest {ObsFileTransaction.BackupsKept} are kept.",
            };
    }

    /// <summary>
    /// Writes <paramref name="backup"/> (a path, or a file name in the backups folder) over the
    /// live collection, while OBS is closed.
    /// </summary>
    public static ObsBackupResult Restore(
        ObsManagedFiles managed,
        string collectionPath,
        string backup,
        bool obsIsRunning,
        DateTime utcNow
    )
    {
        ArgumentNullException.ThrowIfNull(managed);
        string collection = Path.GetFullPath(collectionPath);
        var result = new ObsBackupResult { Collection = collection };
        string source = Resolve(managed, backup);
        if (source == null || !File.Exists(source))
        {
            return result with
            {
                Code = ObsBackupCodes.BackupMissing,
                Message = $"There is no backup '{backup}'. `obs backup --list` shows them.",
            };
        }

        result = result with { Backup = source };
        if (!ObsFileTransaction.IsBackupOf(source, collection))
        {
            return result with
            {
                Code = ObsBackupCodes.BackupOther,
                Message = $"{source} is not a backup of {collection}, so it was not restored.",
            };
        }

        if (obsIsRunning)
        {
            return result with
            {
                Code = ObsBackupCodes.ObsRunning,
                Message =
                    "OBS is running and saves its collection over the file, so nothing was restored. Close OBS and run it again.",
            };
        }

        byte[] contents;
        try
        {
            contents = File.ReadAllBytes(source);
            using JsonDocument document = JsonDocument.Parse(contents);
            if (
                document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("sources", out JsonElement sources)
                || sources.ValueKind != JsonValueKind.Array
            )
            {
                throw new JsonException("It has no sources.");
            }
        }
        catch (JsonException e)
        {
            return result with
            {
                Code = ObsBackupCodes.BackupInvalid,
                Message =
                    $"{source} is not an OBS scene collection, so it was not restored. {e.Message}",
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return result with
            {
                Code = ObsBackupCodes.Failed,
                Message = $"{source} could not be read, so nothing was restored. {e.Message}",
            };
        }

        byte[] current = File.Exists(collection) ? TryRead(collection) : null;
        if (current != null && current.AsSpan().SequenceEqual(contents))
        {
            return result with
            {
                Ok = true,
                Code = ObsBackupCodes.AlreadyRestored,
                Message = $"{collection} already has the bytes of {source}. Nothing was written.",
            };
        }

        string saved;
        try
        {
            saved = ObsFileTransaction.Write(collection, contents, managed.BackupDirectory, utcNow);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return result with
            {
                Code = ObsBackupCodes.Failed,
                Message = $"The collection was not restored and is as it was. {e.Message}",
            };
        }

        // A restore by hand is the latest write: a release rollback that waited for OBS is over.
        managed.ClearPendingRestore(collection);
        return result with
        {
            Ok = true,
            Code = ObsBackupCodes.Restored,
            Saved = saved,
            Backups = Describe(managed, collection),
            Message =
                $"Restored {collection} from {source}."
                + (
                    saved == null
                        ? string.Empty
                        : $" The collection it replaced was saved to {saved}."
                )
                + " managed-collections.json is unchanged: an update treats the restored file as it would the one it replaced.",
        };
    }

    private static string Resolve(ObsManagedFiles managed, string backup)
    {
        if (string.IsNullOrWhiteSpace(backup))
        {
            return null;
        }

        string inFolder = Path.Combine(managed.BackupDirectory, Path.GetFileName(backup));
        return File.Exists(backup) ? Path.GetFullPath(backup)
            : File.Exists(inFolder) ? inFolder
            : null;
    }

    private static IReadOnlyList<ObsBackupInfo> Describe(
        ObsManagedFiles managed,
        string collectionPath
    ) =>
        ObsFileTransaction
            .Backups(managed.BackupDirectory, collectionPath)
            .Select(path =>
            {
                byte[] bytes = TryRead(path);
                return new ObsBackupInfo
                {
                    Path = path,
                    TakenAtUtc = ObsFileTransaction.BackupTime(path),
                    Bytes = bytes?.LongLength ?? 0,
                    Sha256 = bytes == null ? null : Convert.ToHexString(SHA256.HashData(bytes)),
                };
            })
            .ToList();

    private static byte[] TryRead(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
