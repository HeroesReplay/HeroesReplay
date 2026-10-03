using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Obs;

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
/// any install, so it survives a release update: <c>backups\</c> (<see cref="ObsFileTransaction"/>)
/// and <see cref="RecordFileName"/>, one <see cref="ObsManagedCollection"/> per live collection file.
/// </summary>
public sealed class ObsManagedFiles
{
    public const string RecordFileName = "managed-collections.json";

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

    /// <summary>The record for <paramref name="collectionPath"/>, or null when there is none or it cannot be read.</summary>
    public ObsManagedCollection Read(string collectionPath) =>
        ReadAll().TryGetValue(Key(collectionPath), out ObsManagedCollection record) ? record : null;

    public void Save(string collectionPath, ObsManagedCollection record)
    {
        Dictionary<string, ObsManagedCollection> all = ReadAll();
        all[Key(collectionPath)] = record;
        DurableFile.Replace(RecordPath, JsonSerializer.Serialize(all, Json));
    }

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

    private static string Key(string collectionPath) => Path.GetFullPath(collectionPath);
}
