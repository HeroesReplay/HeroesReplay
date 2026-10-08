using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// The hero statistics files, one per major patch and game type, in
/// <c>Data\HeroesProfile\hero-stats\&lt;patch&gt;-&lt;code&gt;.json</c>. The download role writes
/// them; spectate only reads. A file is replaced through a temp file, so a reader never sees half
/// of one. A parsed file is kept in memory until its write time or size changes.
/// </summary>
public sealed class HeroStatsStore
{
    private static readonly object CacheLock = new object();
    private static readonly Dictionary<string, CachedSnapshot> Cache = new Dictionary<
        string,
        CachedSnapshot
    >(StringComparer.OrdinalIgnoreCase);

    public HeroStatsStore(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new ArgumentException("A data directory is required.", nameof(dataDirectory));
        }

        Folder = Path.Combine(dataDirectory, "HeroesProfile", "hero-stats");
    }

    public string Folder { get; }

    /// <summary>The file for this patch (or any build of it) and game type, or null when either is unknown.</summary>
    public string PathFor(string patch, string gameType)
    {
        string name = HeroStatsPatch.FileName(patch, gameType);
        return name == null ? null : Path.Combine(Folder, name);
    }

    /// <summary>The saved snapshot, whatever its age. Null when it is missing or unreadable.</summary>
    public HeroStatsSnapshot Read(string patch, string gameType)
    {
        string path = PathFor(patch, gameType);
        if (path == null)
        {
            return null;
        }

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return null;
        }

        lock (CacheLock)
        {
            if (
                Cache.TryGetValue(path, out CachedSnapshot cached)
                && cached.WrittenUtc == file.LastWriteTimeUtc
                && cached.Length == file.Length
            )
            {
                return cached.Snapshot;
            }
        }

        HeroStatsSnapshot snapshot;
        try
        {
            snapshot = HeroStatsJson.ReadSnapshot(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        lock (CacheLock)
        {
            Cache[path] = new CachedSnapshot(file.LastWriteTimeUtc, file.Length, snapshot);
        }

        return snapshot;
    }

    /// <summary>
    /// The snapshot when it was fetched within <paramref name="maxAge"/> of <paramref name="now"/>.
    /// Null when it is missing, unreadable, stale, or dated in the future.
    /// </summary>
    public HeroStatsSnapshot ReadFresh(
        string patch,
        string gameType,
        DateTimeOffset now,
        TimeSpan maxAge
    )
    {
        HeroStatsSnapshot snapshot = Read(patch, gameType);
        return IsFresh(snapshot, now, maxAge) ? snapshot : null;
    }

    /// <summary>True when the file is missing, unreadable, or older than <paramref name="refreshInterval"/>.</summary>
    public bool IsDue(string patch, string gameType, DateTimeOffset now, TimeSpan refreshInterval)
    {
        return !IsFresh(Read(patch, gameType), now, refreshInterval);
    }

    public string Write(HeroStatsSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        string path =
            PathFor(snapshot.Patch, snapshot.GameType)
            ?? throw new ArgumentException(
                "A snapshot needs a major patch and a game type.",
                nameof(snapshot)
            );
        DurableFile.Replace(path, HeroStatsJson.Write(snapshot));
        return path;
    }

    /// <summary>Deletes every file whose patch is not in <paramref name="keepPatches"/>. Returns the deleted names.</summary>
    public IReadOnlyList<string> Prune(IEnumerable<string> keepPatches)
    {
        var keep = new HashSet<string>(
            (keepPatches ?? Enumerable.Empty<string>())
                .Select(HeroStatsPatch.Major)
                .Where(patch => patch != null),
            StringComparer.Ordinal
        );
        var deleted = new List<string>();
        if (keep.Count == 0 || !Directory.Exists(Folder))
        {
            return deleted;
        }

        foreach (string path in Directory.EnumerateFiles(Folder, "*.json"))
        {
            string name = Path.GetFileName(path);
            string patch = HeroStatsPatch.PatchOfFile(name);
            if (patch == null || keep.Contains(patch))
            {
                continue;
            }

            try
            {
                File.Delete(path);
                deleted.Add(name);
            }
            catch (IOException)
            {
                // Spectate may be reading it. The next pass deletes it.
            }
            catch (UnauthorizedAccessException)
            {
                // Left for the next pass.
            }
        }

        return deleted;
    }

    private static bool IsFresh(HeroStatsSnapshot snapshot, DateTimeOffset now, TimeSpan maxAge)
    {
        if (snapshot == null || maxAge <= TimeSpan.Zero)
        {
            return false;
        }

        TimeSpan age = now - snapshot.FetchedAtUtc;
        return age >= TimeSpan.FromMinutes(-5) && age <= maxAge;
    }

    private sealed record CachedSnapshot(
        DateTime WrittenUtc,
        long Length,
        HeroStatsSnapshot Snapshot
    );
}
