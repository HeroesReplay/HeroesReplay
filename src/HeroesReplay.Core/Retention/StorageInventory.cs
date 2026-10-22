using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.MediaPolicy;

namespace HeroesReplay.Core.Retention;

/// <summary>
/// One kind of file the automation writes, by folder and, inside <c>Data\Contexts</c>, by
/// extension. A file belongs to the category with the deepest folder that holds it; at equal depth
/// one with an extension wins.
/// </summary>
public sealed record StorageCategory(
    string Name,
    string Directory,
    string Description,
    string Extension = null
);

/// <summary>One category in <c>storage report</c>: what it holds, and what the next pass removes from it.</summary>
public sealed class StorageCategoryUsage
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Path { get; set; }
    public int Files { get; set; }
    public long Bytes { get; set; }
    public DateTimeOffset? Oldest { get; set; }
    public int PlannedFiles { get; set; }
    public long PlannedBytes { get; set; }
}

/// <summary>
/// What uses the disk under <c>Location:DataDirectory</c> and <c>%LOCALAPPDATA%\HeroesReplay</c>,
/// by category (#487). It only reads.
/// </summary>
public static class StorageInventory
{
    public const string DataOther = "data-other";
    public const string LocalOther = "local-other";

    private static readonly EnumerationOptions AllFiles = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>Every category, the catch-all of each root last.</summary>
    public static IReadOnlyList<StorageCategory> Categories(
        AppSettings settings,
        string stateDirectory
    )
    {
        var categories = new List<StorageCategory>();
        string data = settings?.Location?.DataDirectory;
        if (!string.IsNullOrWhiteSpace(data))
        {
            string contexts = Path.Combine(data, "Contexts");
            categories.Add(
                new("recordings", contexts, "OBS recordings (mp4) in Data\\Contexts", ".mp4")
            );
            categories.Add(
                new("screenshots", contexts, "End screenshots (png) in Data\\Contexts", ".png")
            );
            categories.Add(new("contexts", contexts, "The rest of each replay's context folder"));
            categories.Add(
                new(
                    "standard",
                    Path.Combine(
                        data,
                        Folder(settings.HeroesProfileApi?.StandardCacheDirectoryName, "Standard")
                    ),
                    "Downloaded Heroes Profile replays"
                )
            );
            categories.Add(
                new(
                    "requests",
                    Path.Combine(
                        data,
                        Folder(settings.HeroesProfileApi?.RequestsCacheDirectoryName, "Requests")
                    ),
                    "Twitch-requested replays and their request sidecars"
                )
            );
            categories.Add(
                new(
                    "clients",
                    string.IsNullOrWhiteSpace(settings.Location.RetainedClientDirectory)
                        ? Path.Combine(data, "Clients")
                        : settings.Location.RetainedClientDirectory,
                    "Retained copies of older client builds"
                )
            );
            categories.Add(
                new("heroes-data", Path.Combine(data, "HeroesData"), "heroes-data2 catalog cache")
            );
            categories.Add(
                new(
                    "upload-attempts",
                    Path.Combine(data, MediaPolicyAttemptLog.AttemptsDirectoryName),
                    "Upload attempt manifests, one folder per evaluated replay"
                )
            );
            categories.Add(new("leases", Path.Combine(data, "leases"), "Replay lease files"));
            categories.Add(
                new(
                    "players",
                    Path.Combine(data, "HeroesProfile", PlayerHeroStatsStore.FolderName),
                    "Heroes Profile player lookups"
                )
            );
            categories.Add(
                new(DataOther, data, "Everything else in Data: ledgers, id lists, reports")
            );
        }

        if (!string.IsNullOrWhiteSpace(stateDirectory))
        {
            categories.Add(
                new("updates", Path.Combine(stateDirectory, "updates"), "Downloaded releases")
            );
            categories.Add(
                new(
                    "logs",
                    string.IsNullOrWhiteSpace(settings?.ServiceLogs?.Directory)
                        ? Path.Combine(stateDirectory, "logs")
                        : settings.ServiceLogs.ResolvedDirectory,
                    "Role, supervisor and release logs"
                )
            );
            categories.Add(
                new("ready", Path.Combine(stateDirectory, "ready"), "Role heartbeat (ready) files")
            );
            categories.Add(
                new(
                    "obs",
                    Path.Combine(stateDirectory, "obs"),
                    "OBS collection backups, templates and asset copies"
                )
            );
            categories.Add(
                new(LocalOther, stateDirectory, "Everything else in %LOCALAPPDATA%\\HeroesReplay")
            );
        }

        return categories;
    }

    /// <summary>The category that holds <paramref name="path"/>, or null when none does.</summary>
    public static StorageCategory Classify(IReadOnlyList<StorageCategory> categories, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string extension = System.IO.Path.GetExtension(path);
        return categories
            .Where(category => Holds(category.Directory, path))
            .Where(category =>
                category.Extension == null
                || string.Equals(category.Extension, extension, StringComparison.OrdinalIgnoreCase)
            )
            .OrderByDescending(category => Trim(category.Directory).Length)
            .ThenByDescending(category => category.Extension != null)
            .FirstOrDefault();
    }

    /// <summary>Counts every file under the categories' folders, once each.</summary>
    public static List<StorageCategoryUsage> Measure(IReadOnlyList<StorageCategory> categories)
    {
        Dictionary<string, StorageCategoryUsage> usage = categories.ToDictionary(
            category => category.Name,
            category => new StorageCategoryUsage
            {
                Name = category.Name,
                Description = category.Description,
                Path = category.Directory,
            },
            StringComparer.OrdinalIgnoreCase
        );
        foreach (string root in Roots(categories))
        {
            foreach (FileInfo file in Files(root))
            {
                if (Classify(categories, file.FullName) is not StorageCategory category)
                {
                    continue;
                }

                StorageCategoryUsage row = usage[category.Name];
                row.Files++;
                row.Bytes += file.Length;
                DateTimeOffset written = file.LastWriteTimeUtc;
                if (row.Oldest == null || written < row.Oldest)
                {
                    row.Oldest = written;
                }
            }
        }

        return categories.Select(category => usage[category.Name]).ToList();
    }

    /// <summary>Adds what <paramref name="plans"/> would remove to the category of each file.</summary>
    public static void AddPlanned(
        IReadOnlyList<StorageCategory> categories,
        IReadOnlyList<StorageCategoryUsage> usage,
        IEnumerable<PrunePlan> plans
    )
    {
        Dictionary<string, StorageCategoryUsage> rows = usage.ToDictionary(
            row => row.Name,
            StringComparer.OrdinalIgnoreCase
        );
        foreach (PruneItem item in plans.SelectMany(plan => plan.TopLevel))
        {
            IEnumerable<(string Path, long Bytes)> files = item.IsDirectory
                ? Files(item.Path).Select(file => (file.FullName, file.Length))
                : [(item.Path, item.Bytes)];
            foreach ((string path, long bytes) in files)
            {
                if (
                    Classify(categories, path) is StorageCategory category
                    && rows.TryGetValue(category.Name, out StorageCategoryUsage row)
                )
                {
                    row.PlannedFiles++;
                    row.PlannedBytes += bytes;
                }
            }
        }
    }

    /// <summary>Free bytes on the drive that holds <paramref name="path"/>, or null when unknown.</summary>
    public static long? FreeBytes(string path)
    {
        try
        {
            string root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception e)
            when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static IEnumerable<string> Roots(IReadOnlyList<StorageCategory> categories)
    {
        List<string> folders = categories
            .Select(category => Trim(category.Directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return folders.Where(folder =>
            !folders.Any(other =>
                !string.Equals(other, folder, StringComparison.OrdinalIgnoreCase)
                && Holds(other, folder)
            )
        );
    }

    private static IEnumerable<FileInfo> Files(string root)
    {
        try
        {
            return Directory.Exists(root)
                ? new DirectoryInfo(root).EnumerateFiles("*", AllFiles).ToList()
                : Enumerable.Empty<FileInfo>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Enumerable.Empty<FileInfo>();
        }
    }

    private static bool Holds(string directory, string path) =>
        !string.IsNullOrWhiteSpace(directory)
        && path.StartsWith(
            Trim(directory) + System.IO.Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase
        );

    private static string Trim(string directory) =>
        System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(directory));

    private static string Folder(string name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name;
}
