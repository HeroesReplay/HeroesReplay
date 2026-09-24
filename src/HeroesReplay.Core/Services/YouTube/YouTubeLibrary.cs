using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.YouTube;

public sealed class YouTubePlaylistCache
{
    public Dictionary<string, string> PlaylistIds { get; set; } = new();
    public List<string> FiledVideoIds { get; set; } = new();
}

public interface IYouTubeLibrary
{
    Task<int> RunOnceAsync(CancellationToken cancellationToken);
}

public class YouTubeLibrary : IYouTubeLibrary
{
    public const string CacheFileName = "youtube-playlists.json";
    public const string DryRunFileName = "youtube-library-dry-run.json";

    private readonly ILogger<YouTubeLibrary> logger;
    private readonly AppSettings settings;
    private readonly IYouTubePlaylistClient playlists;

    public YouTubeLibrary(
        ILogger<YouTubeLibrary> logger,
        AppSettings settings,
        IYouTubePlaylistClient playlists
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.playlists = playlists ?? throw new ArgumentNullException(nameof(playlists));
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        List<YouTubeEntry> entries = ReadUploadedEntries();
        IReadOnlyList<YouTubeLibraryItem> items = YouTubeLibraryPlanner.Select(entries);
        if (settings.YouTube?.DryRun != false)
        {
            await WriteDryRunAsync(entries.Count, items, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "YouTube library dry-run planned {Count} playlist inserts from {Entries} uploaded entries. YouTube was not called.",
                items.Count,
                entries.Count
            );
            return 0;
        }

        YouTubePlaylistCache cache = LoadCache();
        int filed = await FileAsync(items, cache, playlists, cancellationToken)
            .ConfigureAwait(false);
        SaveCache(cache);
        logger.LogInformation("YouTube library filed {Count} videos into playlists.", filed);
        return 0;
    }

    public async Task<int> FileAsync(
        IReadOnlyList<YouTubeLibraryItem> items,
        YouTubePlaylistCache cache,
        IYouTubePlaylistClient client,
        CancellationToken cancellationToken
    )
    {
        if (items == null || items.Count == 0)
        {
            return 0;
        }

        cache ??= new YouTubePlaylistCache();
        cache.PlaylistIds ??= new Dictionary<string, string>(StringComparer.Ordinal);
        cache.FiledVideoIds ??= new List<string>();
        var filed = new HashSet<string>(cache.FiledVideoIds, StringComparer.Ordinal);
        int count = 0;
        foreach (YouTubeLibraryItem item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item == null || string.IsNullOrWhiteSpace(item.VideoId) || !filed.Add(item.VideoId))
            {
                continue;
            }

            try
            {
                if (
                    !cache.PlaylistIds.TryGetValue(item.PlaylistTitle, out string playlistId)
                    || string.IsNullOrWhiteSpace(playlistId)
                )
                {
                    playlistId = await client
                        .FindOrCreateAsync(item.PlaylistTitle, cancellationToken)
                        .ConfigureAwait(false);
                    cache.PlaylistIds[item.PlaylistTitle] = playlistId;
                }

                await client
                    .InsertAsync(playlistId, item.VideoId, cancellationToken)
                    .ConfigureAwait(false);
                cache.FiledVideoIds.Add(item.VideoId);
                count++;
                logger.LogInformation(
                    "Filed {VideoId} into {Playlist}.",
                    item.VideoId,
                    item.PlaylistTitle
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                filed.Remove(item.VideoId);
                logger.LogWarning(
                    exception,
                    "Could not file {VideoId} into {Playlist}.",
                    item.VideoId,
                    item.PlaylistTitle
                );
            }
        }

        return count;
    }

    private List<YouTubeEntry> ReadUploadedEntries()
    {
        var entries = new List<YouTubeEntry>();
        string contexts = ContextsDirectory();
        if (contexts == null || !Directory.Exists(contexts))
        {
            return entries;
        }

        string uploadedName = settings.YouTube?.EntryFileNameUploaded;
        if (string.IsNullOrWhiteSpace(uploadedName))
        {
            uploadedName = "youtube-entry-uploaded.json";
        }

        foreach (
            string path in Directory.EnumerateFiles(
                contexts,
                uploadedName,
                SearchOption.AllDirectories
            )
        )
        {
            try
            {
                YouTubeEntry entry = JsonSerializer.Deserialize<YouTubeEntry>(
                    File.ReadAllText(path)
                );
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                logger.LogWarning(exception, "Could not read YouTube entry {Path}.", path);
            }
        }

        return entries;
    }

    private async Task WriteDryRunAsync(
        int entryCount,
        IReadOnlyList<YouTubeLibraryItem> items,
        CancellationToken cancellationToken
    )
    {
        string directory = DataDirectory();
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, DryRunFileName);
        string json = JsonSerializer.Serialize(
            new
            {
                Simulated = true,
                Entries = entryCount,
                Planned = items.Count,
                Items = items,
            },
            new JsonSerializerOptions { WriteIndented = true }
        );
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    private YouTubePlaylistCache LoadCache()
    {
        string path = CachePath();
        if (path == null || !File.Exists(path))
        {
            return new YouTubePlaylistCache();
        }

        try
        {
            YouTubePlaylistCache cache = JsonSerializer.Deserialize<YouTubePlaylistCache>(
                File.ReadAllText(path)
            );
            return cache ?? new YouTubePlaylistCache();
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            logger.LogWarning(exception, "Ignoring unreadable playlist cache {Path}.", path);
            return new YouTubePlaylistCache();
        }
    }

    private void SaveCache(YouTubePlaylistCache cache)
    {
        string path = CachePath();
        if (path == null)
        {
            return;
        }

        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true })
        );
    }

    private string CachePath()
    {
        string directory = DataDirectory();
        return directory == null ? null : Path.Combine(directory, CacheFileName);
    }

    private string DataDirectory() => settings.Location?.DataDirectory;

    private string ContextsDirectory()
    {
        string directory = DataDirectory();
        return string.IsNullOrWhiteSpace(directory) ? null : Path.Combine(directory, "Contexts");
    }
}
