using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Obs.Collection;

/// <summary>
/// The template a live scene collection was last written from, and its scene and source names
/// then. A live collection that still has exactly those names is HeroesReplay's to replace; one
/// with other names was changed by the operator.
/// </summary>
/// <param name="TemplateSha256">The template's hash (<see cref="ObsCollectionPatcher.HashOf"/>), or <see cref="ObsCollectionPatcher.UnknownTemplate"/>.</param>
/// <param name="Sources">The collection's scene and source names when it was written.</param>
/// <param name="WrittenAtUtc">When it was written.</param>
/// <param name="Merged">
/// The collection was written by a merge that kept the operator's work (#307: <c>obs apply</c>,
/// or a release that merged into a collection where the operator only added), and
/// <paramref name="Sources"/> are the merged collection's names, the operator's included. Such a
/// collection is never replaced with the template: a later update merges again, or keeps it.
/// </param>
public sealed record ObsManagedCollection(
    string TemplateSha256,
    IReadOnlyList<string> Sources,
    DateTime WrittenAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Merged = false
);

/// <summary>
/// What HeroesReplay keeps about the OBS files it writes, outside OBS's own folders and outside
/// any install, so it survives a release update: <c>backups\</c> (<see cref="ObsFileTransaction"/>),
/// <see cref="RecordFileName"/> (one <see cref="ObsManagedCollection"/> per live collection file),
/// <see cref="RollbackFileName"/> (what the last release install needs for a rollback),
/// <see cref="PendingRestoreFileName"/> (a rollback that waits for OBS), <c>templates\</c> (a copy
/// of each collection template HeroesReplay writes from, the base of a three-way merge, #307), and
/// <see cref="ApplyUndoFileName"/> (the record from before the last <c>obs apply</c>).
/// </summary>
public sealed class ObsManagedFiles
{
    public const string RecordFileName = "managed-collections.json";

    /// <summary>Written by each release install: <see cref="ObsReleaseRollback"/>.</summary>
    public const string RollbackFileName = "release-rollback.json";

    /// <summary>Written by a rollback that could not restore the collection yet: <see cref="ObsPendingRestore"/>.</summary>
    public const string PendingRestoreFileName = "restore-pending.json";

    /// <summary>Written by <c>obs apply</c>: <see cref="ObsApplyUndo"/>.</summary>
    public const string ApplyUndoFileName = "apply-undo.json";

    /// <summary>The newest template copies kept, besides every one a record names.</summary>
    public const int TemplatesKept = 10;

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

    public string ApplyUndoPath => Path.Combine(Root, ApplyUndoFileName);

    /// <summary>
    /// <c>templates\&lt;SHA-256&gt;.json</c>: a copy of each collection template HeroesReplay wrote
    /// a live collection from, so a later merge still has its base after the install that
    /// carried it is gone.
    /// </summary>
    public string TemplateDirectory => Path.Combine(Root, "templates");

    /// <summary>
    /// Keeps a copy of <paramref name="template"/> (the text of an <c>obs\Default.json</c>) under
    /// its SHA-256, as <see cref="ObsCollectionPatcher.TemplateHash"/> names it. The newest
    /// <see cref="TemplatesKept"/> copies and every one a record names are kept. Returns the
    /// hash, or null when the copy could not be written: a missing copy only means a later merge
    /// has no base and keeps the collection as it is.
    /// </summary>
    public string SaveTemplate(string template, DateTime utcNow)
    {
        if (string.IsNullOrEmpty(template))
        {
            return null;
        }

        string hash = ObsCollectionPatcher.HashOf(template);
        try
        {
            string path = TemplatePath(hash);
            if (ReadTemplate(hash) == null)
            {
                Directory.CreateDirectory(TemplateDirectory);
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, template);
                File.Move(temp, path, overwrite: true);
            }

            File.SetLastWriteTimeUtc(path, utcNow);
            PruneTemplates();
            return hash;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The stored template with that SHA-256, or null when there is none or its bytes no longer match.</summary>
    public string ReadTemplate(string sha256)
    {
        if (
            string.IsNullOrWhiteSpace(sha256)
            || sha256.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
        )
        {
            return null;
        }

        try
        {
            string path = TemplatePath(sha256);
            if (!File.Exists(path))
            {
                return null;
            }

            string text = File.ReadAllText(path);
            return string.Equals(
                ObsCollectionPatcher.HashOf(text),
                sha256,
                StringComparison.OrdinalIgnoreCase
            )
                ? text
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The record from before the last <c>obs apply</c>, or null.</summary>
    public ObsApplyUndo ReadApplyUndo() =>
        ReadJson<ObsApplyUndo>(ApplyUndoPath) is { CollectionPath: not null, Backup: not null } undo
            ? undo
            : null;

    public void SaveApplyUndo(ObsApplyUndo undo)
    {
        ArgumentNullException.ThrowIfNull(undo);
        DurableFile.Replace(ApplyUndoPath, JsonSerializer.Serialize(undo, Json));
    }

    public void ClearApplyUndo() => Delete(ApplyUndoPath);

    private string TemplatePath(string sha256) =>
        Path.Combine(TemplateDirectory, sha256.ToUpperInvariant() + ".json");

    private void PruneTemplates()
    {
        var named = new HashSet<string>(
            ReadAll().Values.Select(record => record.TemplateSha256),
            StringComparer.OrdinalIgnoreCase
        );
        foreach (
            FileInfo old in new DirectoryInfo(TemplateDirectory)
                .GetFiles("*.json")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(TemplatesKept)
                .Where(file => !named.Contains(Path.GetFileNameWithoutExtension(file.Name)))
        )
        {
            Delete(old.FullName);
        }
    }

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
