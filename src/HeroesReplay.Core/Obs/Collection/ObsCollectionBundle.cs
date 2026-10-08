using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using HeroesReplay.Core.Obs.Inspection;

namespace HeroesReplay.Core.Obs.Collection;

/// <summary>One packaged OBS file: its path under <c>obs/</c> (forward slashes), size, and SHA-256 (hex).</summary>
public sealed record ObsBundleAsset(string Path, long Size, string Sha256);

/// <summary>A source HeroesReplay drives by name, and its kind (<c>id</c>) in <c>obs/Default.json</c>.</summary>
public sealed record ObsBundleSource(string Name, string Kind);

/// <summary>
/// The scenes, sources, and scene items HeroesReplay drives (<see cref="ObsContract"/>), as the
/// release's settings named them when it was packaged, with each source's kind in
/// <c>obs/Default.json</c>.
/// </summary>
public sealed record ObsBundleContract(
    IReadOnlyList<string> Scenes,
    IReadOnlyList<ObsBundleSource> Sources,
    IReadOnlyList<ObsContractItem> Items
);

/// <summary>
/// <c>obs/bundle.manifest</c> as a release ships it (schema 2, JSON): the packaged collection and
/// its SHA-256, every packaged OBS file with its size and SHA-256, and the scene and source
/// contract. In the repository the same file is the plain list of asset paths the build
/// publishes; <c>tools/package-release.ps1</c> turns it into this with <c>obs bundle --write</c>.
/// </summary>
public sealed record ObsBundleManifest
{
    public int SchemaVersion { get; init; }

    /// <summary>The collection template, relative to <c>obs/</c>: <c>Default.json</c>.</summary>
    public string Collection { get; init; }

    public string CollectionSha256 { get; init; }
    public IReadOnlyList<ObsBundleAsset> Assets { get; init; } = [];
    public ObsBundleContract Contract { get; init; }
}

/// <summary>What <c>obs/bundle.manifest</c> is in a folder.</summary>
public enum ObsBundleFormat
{
    /// <summary>No manifest. A release packaged before schema 2 has none.</summary>
    Missing,

    /// <summary>The plain list of asset paths (the repository's file, schema 1). Presence only.</summary>
    PlainList,

    /// <summary>Schema 2: sizes, SHA-256, and the contract.</summary>
    Versioned,
}

/// <summary>A file that does not match the manifest, or a contract name the collection lacks.</summary>
public sealed record ObsBundleProblem(string Path, string Reason);

/// <summary>The outcome of <see cref="ObsCollectionBundle.Verify(string)"/>.</summary>
public sealed record ObsBundleCheck(
    ObsBundleFormat Format,
    string ManifestPath,
    int Files,
    IReadOnlyList<ObsBundleProblem> Problems
)
{
    public bool Ok => Problems.Count == 0;

    public string Describe()
    {
        if (!Ok)
        {
            return ObsCollectionBundle.InvalidCode
                + ": "
                + string.Join("; ", Problems.Take(8).Select(p => "obs/" + p.Path + " " + p.Reason))
                + (Problems.Count > 8 ? " (+" + (Problems.Count - 8) + " more)" : string.Empty)
                + ".";
        }

        return Format switch
        {
            ObsBundleFormat.Versioned => "obs/"
                + ObsCollectionBundle.FileName
                + " (schema "
                + ObsCollectionBundle.SchemaVersion
                + "): "
                + Files
                + " files match their size and SHA-256, and obs/Default.json has the scene and source contract.",
            ObsBundleFormat.PlainList => "obs/"
                + ObsCollectionBundle.FileName
                + " is the plain asset list: "
                + Files
                + " files are present. It has no checksums, so their contents were not checked.",
            _ => "Warning: obs/"
                + ObsCollectionBundle.FileName
                + " is missing, so the OBS files were not checked against their checksums. A release packaged before the versioned manifest has none.",
        };
    }
}

/// <summary>
/// Reads, writes, and checks <c>obs/bundle.manifest</c>. The schema 2 manifest lets a release
/// zip, an install, and <c>obs validate</c> prove that every packaged OBS file is the one that
/// was packaged. The plain list (schema 1) is still read, presence only, and a folder with no
/// manifest is reported but not refused, so an install from before schema 2 can still update.
/// </summary>
public static class ObsCollectionBundle
{
    public const string FileName = "bundle.manifest";
    public const string CollectionFileName = "Default.json";
    public const int SchemaVersion = 2;
    public const string InvalidCode = ObsValidator.BundleInvalid;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The manifest for the files <paramref name="assets"/> names under
    /// <paramref name="obsDirectory"/>, with <paramref name="contract"/> and each contract
    /// source's kind from <c>Default.json</c>.
    /// </summary>
    /// <exception cref="FileNotFoundException">A listed file or <c>Default.json</c> is missing.</exception>
    public static ObsBundleManifest Create(
        string obsDirectory,
        IEnumerable<string> assets,
        ObsContract contract
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(obsDirectory);
        ArgumentNullException.ThrowIfNull(assets);
        string root = Path.GetFullPath(obsDirectory);
        string collectionPath = Path.Combine(root, CollectionFileName);
        if (!File.Exists(collectionPath))
        {
            throw new FileNotFoundException(
                "obs/" + CollectionFileName + " is missing.",
                collectionPath
            );
        }

        var files = new List<ObsBundleAsset>();
        foreach (
            string path in assets
                .Select(Normalize)
                .Where(path => path.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
        )
        {
            string full = FullPath(root, path);
            if (full == null || !File.Exists(full))
            {
                throw new FileNotFoundException("obs/" + path + " is listed but missing.", full);
            }

            files.Add(new ObsBundleAsset(path, new FileInfo(full).Length, Sha256(full)));
        }

        IReadOnlyDictionary<string, string> kinds = ObsCollectionPaths.SourceKinds(
            File.ReadAllText(collectionPath)
        );
        contract ??= new ObsContract([], [], []);
        return new ObsBundleManifest
        {
            SchemaVersion = SchemaVersion,
            Collection = CollectionFileName,
            CollectionSha256 = Sha256(collectionPath),
            Assets = files,
            Contract = new ObsBundleContract(
                contract.Scenes.ToList(),
                contract
                    .Sources.Select(name => new ObsBundleSource(
                        name,
                        kinds.TryGetValue(name, out string kind) ? kind : null
                    ))
                    .ToList(),
                contract.Items.ToList()
            ),
        };
    }

    public static string Serialize(ObsBundleManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, Json) + Environment.NewLine;
    }

    /// <exception cref="JsonException">The text is not a schema 2 manifest.</exception>
    public static ObsBundleManifest Parse(string text) =>
        JsonSerializer.Deserialize<ObsBundleManifest>(text, Json)
        ?? throw new JsonException("The manifest is empty.");

    /// <summary>The asset paths of the manifest in <paramref name="obsDirectory"/>, in either format.</summary>
    public static IReadOnlyList<string> AssetPaths(string obsDirectory)
    {
        string text = File.ReadAllText(Path.Combine(obsDirectory, FileName));
        return IsVersioned(text)
            ? Parse(text).Assets?.Select(asset => asset.Path).ToList() ?? []
            : PlainList(text);
    }

    /// <summary>
    /// Checks <paramref name="obsDirectory"/> against its <c>bundle.manifest</c>: every file's
    /// size and SHA-256 and the collection's contract (schema 2), or that every listed file is
    /// there (the plain list). A folder with no manifest has no problems and
    /// <see cref="ObsBundleFormat.Missing"/>.
    /// </summary>
    public static ObsBundleCheck Verify(string obsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(obsDirectory);
        string root = Path.GetFullPath(obsDirectory);
        string manifestPath = Path.Combine(root, FileName);
        if (!File.Exists(manifestPath))
        {
            return new ObsBundleCheck(ObsBundleFormat.Missing, manifestPath, 0, []);
        }

        string text;
        try
        {
            text = File.ReadAllText(manifestPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Invalid(
                ObsBundleFormat.Versioned,
                manifestPath,
                "could not be read. " + e.Message
            );
        }

        if (!IsVersioned(text))
        {
            IReadOnlyList<string> listed = PlainList(text);
            var missing = listed
                .Where(path => FullPath(root, path) is not { } full || !File.Exists(full))
                .Select(path => new ObsBundleProblem(path, "is listed but missing"))
                .ToList();
            return new ObsBundleCheck(
                ObsBundleFormat.PlainList,
                manifestPath,
                listed.Count,
                missing
            );
        }

        ObsBundleManifest manifest;
        try
        {
            manifest = Parse(text);
        }
        catch (JsonException e)
        {
            return Invalid(
                ObsBundleFormat.Versioned,
                manifestPath,
                "is not valid JSON. " + e.Message
            );
        }

        return Verify(root, manifest, manifestPath);
    }

    /// <summary>Checks <paramref name="obsDirectory"/> against <paramref name="manifest"/>.</summary>
    public static ObsBundleCheck Verify(
        string obsDirectory,
        ObsBundleManifest manifest,
        string manifestPath = null
    )
    {
        ArgumentNullException.ThrowIfNull(manifest);
        string root = Path.GetFullPath(obsDirectory);
        manifestPath ??= Path.Combine(root, FileName);
        if (manifest.SchemaVersion != SchemaVersion)
        {
            return Invalid(
                ObsBundleFormat.Versioned,
                manifestPath,
                "has schemaVersion "
                    + manifest.SchemaVersion
                    + ", and this build reads schemaVersion "
                    + SchemaVersion
            );
        }

        IReadOnlyList<ObsBundleAsset> assets = manifest.Assets ?? [];
        if (assets.Count == 0)
        {
            return Invalid(ObsBundleFormat.Versioned, manifestPath, "lists no files");
        }

        var problems = new List<ObsBundleProblem>();
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ObsBundleAsset asset in assets)
        {
            string path = Normalize(asset?.Path);
            string full = FullPath(root, path);
            if (full == null)
            {
                problems.Add(
                    new ObsBundleProblem(asset?.Path ?? "(none)", "is not a path inside obs/")
                );
                continue;
            }

            if (!File.Exists(full))
            {
                problems.Add(new ObsBundleProblem(path, "is missing"));
                continue;
            }

            long size = new FileInfo(full).Length;
            if (size != asset.Size)
            {
                problems.Add(
                    new ObsBundleProblem(
                        path,
                        "is " + size + " bytes, but the manifest says " + asset.Size
                    )
                );
                continue;
            }

            string hash = Sha256(full);
            hashes[path] = hash;
            if (!string.Equals(hash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(
                    new ObsBundleProblem(
                        path,
                        "has SHA-256 " + hash + ", but the manifest says " + asset.Sha256
                    )
                );
            }
        }

        string collection = Normalize(manifest.Collection);
        if (collection.Length == 0)
        {
            collection = CollectionFileName;
        }

        string collectionPath = FullPath(root, collection);
        if (collectionPath == null || !File.Exists(collectionPath))
        {
            if (!problems.Any(problem => problem.Path == collection))
            {
                problems.Add(new ObsBundleProblem(collection, "is missing"));
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(manifest.CollectionSha256))
            {
                string hash = hashes.TryGetValue(collection, out string known)
                    ? known
                    : Sha256(collectionPath);
                if (
                    !string.Equals(
                        hash,
                        manifest.CollectionSha256,
                        StringComparison.OrdinalIgnoreCase
                    ) && !problems.Any(problem => problem.Path == collection)
                )
                {
                    problems.Add(
                        new ObsBundleProblem(
                            collection,
                            "has SHA-256 "
                                + hash
                                + ", but the manifest's collectionSha256 is "
                                + manifest.CollectionSha256
                        )
                    );
                }
            }

            CheckContract(collectionPath, manifest.Contract, problems);
        }

        return new ObsBundleCheck(ObsBundleFormat.Versioned, manifestPath, assets.Count, problems);
    }

    /// <summary>SHA-256 of the file's bytes, upper-case hex (as <c>Get-FileHash</c> prints it).</summary>
    public static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>True when the text is a schema 2 (JSON) manifest rather than the plain list.</summary>
    public static bool IsVersioned(string text) =>
        text != null && text.TrimStart('﻿', ' ', '\t', '\r', '\n').StartsWith('{');

    private static IReadOnlyList<string> PlainList(string text) =>
        text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void CheckContract(
        string collectionPath,
        ObsBundleContract contract,
        List<ObsBundleProblem> problems
    )
    {
        if (contract == null)
        {
            return;
        }

        string json;
        IReadOnlyList<string> scenes;
        IReadOnlyDictionary<string, string> kinds;
        IReadOnlyDictionary<string, IReadOnlyList<string>> items;
        try
        {
            json = File.ReadAllText(collectionPath);
            scenes = ObsCollectionPaths.SceneNames(json);
            kinds = ObsCollectionPaths.SourceKinds(json);
            items = ObsCollectionPaths.SceneItemNames(json);
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            problems.Add(
                new ObsBundleProblem(CollectionFileName, "could not be read. " + e.Message)
            );
            return;
        }

        var sceneSet = new HashSet<string>(scenes, StringComparer.Ordinal);
        foreach (string scene in contract.Scenes ?? [])
        {
            if (!string.IsNullOrWhiteSpace(scene) && !sceneSet.Contains(scene))
            {
                problems.Add(
                    new ObsBundleProblem(
                        CollectionFileName,
                        "has no scene '" + scene + "', which the manifest's contract names"
                    )
                );
            }
        }

        foreach (ObsBundleSource source in contract.Sources ?? [])
        {
            if (source == null || string.IsNullOrWhiteSpace(source.Name))
            {
                continue;
            }

            if (!kinds.TryGetValue(source.Name, out string kind))
            {
                problems.Add(
                    new ObsBundleProblem(
                        CollectionFileName,
                        "has no source '" + source.Name + "', which the manifest's contract names"
                    )
                );
            }
            else if (
                source.Kind != null
                && !string.Equals(kind, source.Kind, StringComparison.Ordinal)
            )
            {
                problems.Add(
                    new ObsBundleProblem(
                        CollectionFileName,
                        "has source '"
                            + source.Name
                            + "' as a "
                            + kind
                            + ", but the manifest's contract says "
                            + source.Kind
                    )
                );
            }
        }

        foreach (ObsContractItem item in contract.Items ?? [])
        {
            if (
                item == null
                || !sceneSet.Contains(item.Scene ?? string.Empty)
                || !kinds.ContainsKey(item.Source ?? string.Empty)
            )
            {
                // A missing scene or source is already its own problem.
                continue;
            }

            if (
                !items.TryGetValue(item.Scene, out IReadOnlyList<string> names)
                || !names.Contains(item.Source, StringComparer.Ordinal)
            )
            {
                problems.Add(
                    new ObsBundleProblem(
                        CollectionFileName,
                        "has no item '"
                            + item.Source
                            + "' in scene '"
                            + item.Scene
                            + "', which the manifest's contract names"
                    )
                );
            }
        }
    }

    private static ObsBundleCheck Invalid(
        ObsBundleFormat format,
        string manifestPath,
        string reason
    ) => new(format, manifestPath, 0, [new ObsBundleProblem(FileName, reason)]);

    private static string Normalize(string path) =>
        (path ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');

    /// <summary>The file under <paramref name="root"/>, or null for a path that leaves it.</summary>
    private static string FullPath(string root, string relative)
    {
        if (
            string.IsNullOrEmpty(relative)
            || Path.IsPathRooted(relative)
            || relative.Split('/').Any(part => part == "..")
        )
        {
            return null;
        }

        string full = Path.GetFullPath(
            Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))
        );
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}

/// <summary>
/// <c>update install-obs</c> refused a release whose OBS files do not match its manifest. Nothing
/// was written to OBS's folders.
/// </summary>
public sealed class ObsBundleInvalidException : Exception
{
    public ObsBundleInvalidException(ObsBundleCheck check)
        : base(
            (check ?? throw new ArgumentNullException(nameof(check))).Describe()
                + " The OBS collection and profile were not written. Install the release again."
        )
    {
        Check = check;
    }

    public string Code => ObsCollectionBundle.InvalidCode;

    public ObsBundleCheck Check { get; }
}
