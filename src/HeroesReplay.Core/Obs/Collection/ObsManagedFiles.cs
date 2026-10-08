using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Obs.Collection;

/// <summary>
/// The template a live scene collection was last written from, and its scene and source names
/// then. A live collection that still has exactly those names is HeroesReplay's to replace; one
/// with other names was changed by the operator.
/// </summary>
public sealed record ObsManagedCollection(
    string TemplateSha256,
    IReadOnlyList<string> Sources,
    DateTime WrittenAtUtc
);

/// <summary>
/// What HeroesReplay keeps about the OBS files it writes, outside OBS's own folders and outside
/// any install, so it survives a release update: <c>backups\</c> (<see cref="ObsFileTransaction"/>),
/// <see cref="RecordFileName"/> (one <see cref="ObsManagedCollection"/> per live collection file),
/// <see cref="RollbackFileName"/> (what the last release install needs for a rollback), and
/// <see cref="PendingRestoreFileName"/> (a rollback that waits for OBS).
/// </summary>
public sealed class ObsManagedFiles
{
    public const string RecordFileName = "managed-collections.json";

    /// <summary>Written by each release install: <see cref="ObsReleaseRollback"/>.</summary>
    public const string RollbackFileName = "release-rollback.json";

    /// <summary>Written by a rollback that could not restore the collection yet: <see cref="ObsPendingRestore"/>.</summary>
    public const string PendingRestoreFileName = "restore-pending.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public ObsManagedFiles(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = root;
    }

    /// <summary><c>%LOCALAPPDATA%\HeroesReplay\obs</c>.</summary>
    public static ObsManagedFiles ForThisUser() =>
        new(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HeroesReplay",
                "obs"
            )
        );

    public string Root { get; }

    public string BackupDirectory => Path.Combine(Root, "backups");

    public string RecordPath => Path.Combine(Root, RecordFileName);

    public string RollbackPath => Path.Combine(Root, RollbackFileName);

    public string PendingRestorePath => Path.Combine(Root, PendingRestoreFileName);

    /// <summary>The record for <paramref name="collectionPath"/>, or null when there is none or it cannot be read.</summary>
    public ObsManagedCollection Read(string collectionPath) =>
        ReadAll().TryGetValue(Key(collectionPath), out ObsManagedCollection record) ? record : null;

    public void Save(string collectionPath, ObsManagedCollection record)
    {
        Dictionary<string, ObsManagedCollection> all = ReadAll();
        all[Key(collectionPath)] = record;
        DurableFile.Replace(RecordPath, JsonSerializer.Serialize(all, Json));
    }

    /// <summary>Saves <paramref name="record"/>, or forgets the collection when it is null.</summary>
    public void Put(string collectionPath, ObsManagedCollection record)
    {
        if (record != null)
        {
            Save(collectionPath, record);
            return;
        }

        Dictionary<string, ObsManagedCollection> all = ReadAll();
        if (all.Remove(Key(collectionPath)))
        {
            DurableFile.Replace(RecordPath, JsonSerializer.Serialize(all, Json));
        }
    }

    /// <summary>The last release install's rollback record, or null.</summary>
    public ObsReleaseRollback ReadRollback() =>
        ReadJson<ObsReleaseRollback>(RollbackPath) is { CollectionPath: not null } rollback
            ? rollback
            : null;

    public void SaveRollback(ObsReleaseRollback rollback)
    {
        ArgumentNullException.ThrowIfNull(rollback);
        DurableFile.Replace(RollbackPath, JsonSerializer.Serialize(rollback, Json));
    }

    public void ClearRollback() => Delete(RollbackPath);

    /// <summary>The rollback that waits for OBS, or null.</summary>
    public ObsPendingRestore ReadPendingRestore() =>
        ReadJson<ObsPendingRestore>(PendingRestorePath)
            is { CollectionPath: not null, Backup: not null } pending
            ? pending
            : null;

    public void SavePendingRestore(ObsPendingRestore pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        DurableFile.Replace(PendingRestorePath, JsonSerializer.Serialize(pending, Json));
    }

    /// <summary>Forgets the pending restore, only when it is for <paramref name="collectionPath"/>.</summary>
    public void ClearPendingRestore(string collectionPath)
    {
        ObsPendingRestore pending = ReadPendingRestore();
        if (pending != null && SamePath(pending.CollectionPath, collectionPath))
        {
            Delete(PendingRestorePath);
        }
    }

    public static bool SamePath(string left, string right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(Key(left), Key(right), StringComparison.OrdinalIgnoreCase);

    private Dictionary<string, ObsManagedCollection> ReadAll()
    {
        var all = new Dictionary<string, ObsManagedCollection>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(RecordPath))
            {
                foreach (
                    (string path, ObsManagedCollection record) in JsonSerializer.Deserialize<
                        Dictionary<string, ObsManagedCollection>
                    >(File.ReadAllText(RecordPath)) ?? []
                )
                {
                    if (
                        record?.Sources != null
                        && !string.IsNullOrWhiteSpace(record.TemplateSha256)
                    )
                    {
                        all[path] = record;
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Without the record a live collection is compared with the template instead.
        }

        return all;
    }

    private static T ReadJson<T>(string path)
        where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void Delete(string path)
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
            // A stale file is checked against the install's template before it is used.
        }
    }

    private static string Key(string collectionPath) => Path.GetFullPath(collectionPath);
}
