using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeroesReplay.Core.Obs.Collection;

/// <summary>What <see cref="ObsAssetStore.Ensure"/> found or made.</summary>
/// <param name="Ok">The copy is in place and matches the bundle.</param>
/// <param name="AssetRoot">The copy's folder; null when <paramref name="Ok"/> is false.</param>
/// <param name="BundleHash">The bundle's hash, which names the copy's folder.</param>
/// <param name="Copied">This call copied or repaired files.</param>
public sealed record ObsAssetCopy(
    bool Ok,
    string AssetRoot,
    string BundleHash,
    bool Copied,
    string Message
);

/// <summary>
/// Stable, versioned copies of an install's OBS files, so the live collection never points
/// at a folder that goes away: <c>%LOCALAPPDATA%\HeroesReplay\obs\assets\&lt;bundle-hash&gt;\</c>
/// (#330). On a development machine builds run from short-lived git worktrees, and a collection
/// that pointed at one showed missing images once the worktree was removed.
/// <list type="bullet">
/// <item>The bundle is the install's <c>obs\bundle.manifest</c>: schema 2 lists every file's size
/// and SHA-256 (a release); the plain list (a source checkout) is hashed from the files.</item>
/// <item>A copy is made in a staging folder, every file is checked against its SHA-256, and only
/// then is it renamed into place, so a half-made copy is never used and two processes may race.</item>
/// <item><see cref="Prune"/> removes a copy only when no collection or kept backup points at it
/// and it was not used for <see cref="KeepUnused"/>.</item>
/// </list>
/// </summary>
public sealed class ObsAssetStore
{
    public const string FolderName = "assets";

    /// <summary>
    /// Written last into a finished copy: the bundle's file list. Its write time is the copy's
    /// last use.
    /// </summary>
    public const string MarkerFileName = ".bundle";

    /// <summary>Hex digits of the bundle hash that name a copy's folder.</summary>
    public const int HashLength = 16;

    /// <summary>A copy nothing points at is kept this long after its last use.</summary>
    public static readonly TimeSpan KeepUnused = TimeSpan.FromDays(2);

    private const string StagingPrefix = ".staging-";
    private static readonly TimeSpan StagingAbandoned = TimeSpan.FromHours(1);
    private static readonly Regex CopyName = new(
        "^[0-9A-F]{" + HashLength + "}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    public ObsAssetStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    /// <summary><c>&lt;managed root&gt;\assets</c>: <c>%LOCALAPPDATA%\HeroesReplay\obs\assets</c> for this user.</summary>
    public static ObsAssetStore For(ObsManagedFiles managed)
    {
        ArgumentNullException.ThrowIfNull(managed);
        return new ObsAssetStore(Path.Combine(managed.Root, FolderName));
    }

    public string Root { get; }

    /// <summary>
    /// <see cref="Root"/> as a <see cref="ObsCollectionPaths.Rewrite(string, string, string, IReadOnlyList{string})"/>
    /// root: any copy's files move to the asset root a collection is pointed at.
    /// </summary>
    public string AnyCopy => Path.Combine(Root, "*");

    /// <summary>
    /// The copy <see cref="Ensure"/> would use for the bundle in <paramref name="obsDirectory"/>,
    /// without copying anything; null when the bundle cannot be read.
    /// </summary>
    public string Planned(string obsDirectory)
    {
        Bundle bundle = Bundle.Read(obsDirectory, out _);
        return bundle == null ? null : Path.Combine(Root, bundle.Hash);
    }

    /// <summary>
    /// Makes sure a verified copy of the bundle in <paramref name="obsDirectory"/> exists and
    /// returns it. A file that is missing or the wrong size in an existing copy is copied again.
    /// Nothing outside <see cref="Root"/> is written.
    /// </summary>
    public ObsAssetCopy Ensure(string obsDirectory, DateTime utcNow)
    {
        Bundle bundle = Bundle.Read(obsDirectory, out string problem);
        if (bundle == null)
        {
            return new ObsAssetCopy(false, null, null, false, problem);
        }

        string target = Path.Combine(Root, bundle.Hash);
        try
        {
            if (!Directory.Exists(target))
            {
                string staged = Stage(bundle, utcNow);
                try
                {
                    Directory.Move(staged, target);
                    return new ObsAssetCopy(
                        true,
                        target,
                        bundle.Hash,
                        true,
                        "Copied the OBS files to " + target + "."
                    );
                }
                catch (IOException) when (Directory.Exists(target))
                {
                    // Another process made the same copy first; use and check that one.
                    TryDelete(staged);
                }
            }

            bool repaired = Repair(bundle, target, utcNow);
            return new ObsAssetCopy(
                true,
                target,
                bundle.Hash,
                repaired,
                repaired
                    ? "Repaired the OBS files in " + target + "."
                    : "The OBS files are in " + target + "."
            );
        }
        catch (Exception e)
            when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new ObsAssetCopy(
                false,
                null,
                bundle.Hash,
                false,
                "The OBS files could not be copied to " + target + ". " + e.Message
            );
        }
    }

    /// <summary>
    /// Deletes copies that nothing points at: no text in <paramref name="references"/> (the live
    /// collections and the kept backups) names them, they are not <paramref name="keep"/>, and
    /// they were last used more than <see cref="KeepUnused"/> ago. Also clears staging folders
    /// left by a process that died. A copy that cannot be deleted (OBS has a file open) is left.
    /// </summary>
    public IReadOnlyList<string> Prune(IEnumerable<string> references, string keep, DateTime utcNow)
    {
        var deleted = new List<string>();
        if (!Directory.Exists(Root))
        {
            return deleted;
        }

        string referenced = string.Join(
                "\n",
                (references ?? []).Where(text => !string.IsNullOrEmpty(text))
            )
            .Replace(@"\\", "/", StringComparison.Ordinal)
            .Replace('\\', '/');
        string store = "/" + Path.GetFileName(Root) + "/";
        foreach (string folder in Directory.EnumerateDirectories(Root))
        {
            string name = Path.GetFileName(folder);
            if (name.StartsWith(StagingPrefix, StringComparison.Ordinal))
            {
                if (utcNow - Directory.GetCreationTimeUtc(folder) > StagingAbandoned)
                {
                    TryDelete(folder);
                }

                continue;
            }

            if (
                !CopyName.IsMatch(name)
                || string.Equals(name, keep, StringComparison.OrdinalIgnoreCase)
                || referenced.Contains(store + name + "/", StringComparison.OrdinalIgnoreCase)
                || utcNow - LastUse(folder) < KeepUnused
            )
            {
                continue;
            }

            if (TryDelete(folder))
            {
                deleted.Add(folder);
            }
        }

        return deleted;
    }

    /// <summary>The texts <see cref="Prune"/> protects: every collection beside <paramref name="collectionPath"/> and every kept backup.</summary>
    public static IReadOnlyList<string> References(string collectionPath, ObsManagedFiles managed)
    {
        var texts = new List<string>();
        foreach (
            string directory in new[]
            {
                string.IsNullOrWhiteSpace(collectionPath)
                    ? null
                    : Path.GetDirectoryName(Path.GetFullPath(collectionPath)),
                managed?.BackupDirectory,
            }
        )
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                try
                {
                    texts.Add(File.ReadAllText(file));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A file that cannot be read protects nothing; the age rule still does.
                }
            }
        }

        return texts;
    }

    private string Stage(Bundle bundle, DateTime utcNow)
    {
        string staged = Path.Combine(Root, StagingPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staged);
        try
        {
            foreach (BundleFile file in bundle.Files)
            {
                CopyVerified(bundle.Source, file, staged);
            }

            WriteMarker(staged, bundle, utcNow);
            return staged;
        }
        catch
        {
            TryDelete(staged);
            throw;
        }
    }

    /// <summary>
    /// Copies again any file of <paramref name="target"/> that is missing or the wrong size, and
    /// checks every file's SHA-256 when the marker does not list this bundle. True when anything
    /// was copied.
    /// </summary>
    private static bool Repair(Bundle bundle, string target, DateTime utcNow)
    {
        string marker = Path.Combine(target, MarkerFileName);
        bool listed =
            File.Exists(marker)
            && string.Equals(File.ReadAllText(marker), bundle.Listing, StringComparison.Ordinal);
        bool copied = false;
        foreach (BundleFile file in bundle.Files)
        {
            string path = Combine(target, file.Path);
            bool fine =
                File.Exists(path)
                && new FileInfo(path).Length == file.Size
                && (
                    listed
                    || string.Equals(
                        ObsCollectionBundle.Sha256(path),
                        file.Sha256,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            if (!fine)
            {
                CopyVerified(bundle.Source, file, target);
                copied = true;
            }
        }

        WriteMarker(target, bundle, utcNow);
        return copied;
    }

    private static void CopyVerified(string source, BundleFile file, string destinationRoot)
    {
        string from = Combine(source, file.Path);
        string to = Combine(destinationRoot, file.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(to));
        string temp = to + ".copying";
        File.Copy(from, temp, overwrite: true);
        string hash = ObsCollectionBundle.Sha256(temp);
        if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temp);
            throw new InvalidDataException(
                "obs/"
                    + file.Path
                    + " copied with SHA-256 "
                    + hash
                    + ", but the bundle says "
                    + file.Sha256
                    + "."
            );
        }

        File.Move(temp, to, overwrite: true);
    }

    private static void WriteMarker(string folder, Bundle bundle, DateTime utcNow)
    {
        string marker = Path.Combine(folder, MarkerFileName);
        if (
            !File.Exists(marker)
            || !string.Equals(File.ReadAllText(marker), bundle.Listing, StringComparison.Ordinal)
        )
        {
            File.WriteAllText(marker, bundle.Listing);
        }

        File.SetLastWriteTimeUtc(marker, utcNow);
    }

    private static DateTime LastUse(string folder)
    {
        string marker = Path.Combine(folder, MarkerFileName);
        return File.Exists(marker)
            ? File.GetLastWriteTimeUtc(marker)
            : Directory.GetLastWriteTimeUtc(folder);
    }

    private static bool TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The file under <paramref name="root"/>, or an exception for a path that leaves it.</summary>
    private static string Combine(string root, string relative)
    {
        string full = Path.GetFullPath(
            Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))
        );
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? full
            : throw new InvalidDataException("obs/" + relative + " is not a path inside obs/.");
    }

    private sealed record BundleFile(string Path, long Size, string Sha256);

    /// <summary>The files of one install's bundle, and the hash that names their copy.</summary>
    private sealed record Bundle(
        string Source,
        IReadOnlyList<BundleFile> Files,
        string Listing,
        string Hash
    )
    {
        public static Bundle Read(string obsDirectory, out string problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(obsDirectory))
            {
                problem = "There is no obs folder.";
                return null;
            }

            string source = System.IO.Path.GetFullPath(obsDirectory);
            string manifest = System.IO.Path.Combine(source, ObsCollectionBundle.FileName);
            if (!File.Exists(manifest))
            {
                problem =
                    manifest
                    + " is missing, so the OBS files cannot be checked and were not copied.";
                return null;
            }

            try
            {
                string text = File.ReadAllText(manifest);
                List<BundleFile> files = ObsCollectionBundle.IsVersioned(text)
                    ? (ObsCollectionBundle.Parse(text).Assets ?? [])
                        .Select(asset => new BundleFile(
                            Normalize(asset?.Path),
                            asset?.Size ?? 0,
                            (asset?.Sha256 ?? string.Empty).ToUpperInvariant()
                        ))
                        .ToList()
                    : ObsCollectionBundle
                        .AssetPaths(source)
                        .Select(path =>
                        {
                            // The plain list has no hashes: the source build's files are the truth.
                            string full = Combine(source, Normalize(path));
                            return new BundleFile(
                                Normalize(path),
                                new FileInfo(full).Length,
                                ObsCollectionBundle.Sha256(full)
                            );
                        })
                        .ToList();
                if (files.Count == 0)
                {
                    problem = manifest + " lists no files.";
                    return null;
                }

                files = files
                    .DistinctBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(file => file.Path, StringComparer.Ordinal)
                    .ToList();
                string listing = string.Concat(
                    files.Select(file => file.Path + "\t" + file.Size + "\t" + file.Sha256 + "\n")
                );
                string hash = Convert
                    .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(listing)))
                    .Substring(0, HashLength);
                return new Bundle(source, files, listing, hash);
            }
            catch (Exception e)
                when (e
                        is IOException
                            or UnauthorizedAccessException
                            or JsonException
                            or InvalidDataException
                )
            {
                problem = "The OBS bundle in " + source + " could not be read. " + e.Message;
                return null;
            }
        }

        private static string Normalize(string path) =>
            (path ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
    }
}
