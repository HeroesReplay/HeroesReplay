using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube.Publication;
using HeroesReplay.Core.YouTube.Search;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.YouTube.Playlists;

public sealed class YouTubePlaylistCache
{
    public Dictionary<string, string> PlaylistIds { get; set; } = new();
    public List<string> FiledVideoIds { get; set; } = new();
}

/// <summary>
/// What one library pass did. <see cref="Skipped"/> is set when the pass did not run.
/// </summary>
public sealed class YouTubeLibraryPass
{
    public string Skipped { get; set; }
    public bool DryRun { get; set; }
    public int NewVideos { get; set; }
    public int Recorded { get; set; }
    public int Unresolved { get; set; }
    public int Filed { get; set; }
    public int UnitsSpent { get; set; }
    public IReadOnlyList<YouTubeLibraryItem> Planned { get; set; } =
        Array.Empty<YouTubeLibraryItem>();
}

public interface IYouTubeLibrary
{
    /// <summary>
    /// Runs the library pass. Without <paramref name="force"/> it returns at once when the
    /// last pass was inside <c>YouTube:LibraryInterval</c>.
    /// </summary>
    Task<YouTubeLibraryPass> RunOnceAsync(bool force, CancellationToken cancellationToken);
}

/// <summary>
/// The uploader's housekeeping. It lists the channel's uploads, records every video that
/// <c>Data\youtube-library.jsonl</c> does not have yet (Heroes Profile fills a missing map,
/// mode, rank, or build), adds the replay ids to the duplicate catalog, and files the record
/// into the playlist groups that <c>YouTube:Playlists</c> turns on (map, mode, rank, draft,
/// viewer review, patch). Each call reserves its units in <see cref="YouTubeQuotaUnits"/>
/// first and the pass stops when the day's library room is gone.
/// </summary>
public class YouTubeLibrary : IYouTubeLibrary
{
    public const string CacheFileName = "youtube-playlists.json";
    public const string DryRunFileName = "youtube-library-dry-run.json";
    public const string LockFileName = "youtube-library.lock";

    private const int FailuresBeforeStop = 3;

    private readonly ILogger<YouTubeLibrary> logger;
    private readonly AppSettings settings;
    private readonly IYouTubePlaylistClient playlists;
    private readonly IHeroesProfileService heroesProfile;

    public YouTubeLibrary(
        ILogger<YouTubeLibrary> logger,
        AppSettings settings,
        IYouTubePlaylistClient playlists,
        IHeroesProfileService heroesProfile = null
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.playlists = playlists ?? throw new ArgumentNullException(nameof(playlists));
        this.heroesProfile = heroesProfile;
    }

    /// <summary>
    /// Tests move the clock. Production reads the system clock.
    /// </summary>
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    public async Task<YouTubeLibraryPass> RunOnceAsync(
        bool force,
        CancellationToken cancellationToken
    )
    {
        string data = settings.Location?.DataDirectory;
        if (string.IsNullOrWhiteSpace(data))
        {
            return new YouTubeLibraryPass { Skipped = "Location:DataDirectory is not set" };
        }

        DateTimeOffset now = Clock();
        var units = new YouTubeQuotaUnits(data, settings.YouTube);
        if (settings.YouTube?.DryRun != false)
        {
            return await DryRunAsync(data, units, now, cancellationToken).ConfigureAwait(false);
        }

        using FileStream held = DurableFile.TryLock(Path.Combine(data, LockFileName));
        if (held == null)
        {
            logger.LogInformation("YouTube library pass skipped. Another process is running it.");
            return new YouTubeLibraryPass { Skipped = "another process is running the pass" };
        }

        string indexPath = YouTubeUploadsIndex.PathFor(data);
        YouTubeUploadsIndex index = YouTubeUploadsIndex.Load(indexPath);
        if (!force && !index.IsDue(now, settings.YouTube.LibraryInterval))
        {
            return new YouTubeLibraryPass { Skipped = "the last pass is inside LibraryInterval" };
        }

        YouTubeQuotaDay day = units.Read(now);
        if (YouTubeQuotaUnits.Paused(day, now))
        {
            logger.LogInformation(
                "YouTube library pass is paused by a quota response until {ResumeAt:o}.",
                day.LibraryPausedUntil
            );
            return new YouTubeLibraryPass
            {
                Skipped = $"paused by a quota response until {day.LibraryPausedUntil:o}",
            };
        }

        index.LastRunAt = now;
        index.Save(indexPath);
        var pass = new YouTubeLibraryPass();
        var budget = new Budget(units, Clock, pass);
        string recordPath = YouTubeLibraryRecord.PathFor(data);
        try
        {
            Dictionary<string, YouTubeLibraryVideo> record = YouTubeLibraryRecord.Read(recordPath);
            await ListUploadsAsync(index, record, recordPath, budget, pass, now, cancellationToken)
                .ConfigureAwait(false);
            await RefreshScheduledAsync(record, recordPath, budget, now, cancellationToken)
                .ConfigureAwait(false);
            await ResolveAsync(index, record, recordPath, pass, now, cancellationToken)
                .ConfigureAwait(false);
            index.Save(indexPath);

            YouTubePlaylistCache cache = LoadCache();
            pass.Planned = Pending(Plan(record), cache);
            pass.Filed = await FileAsync(
                    pass.Planned,
                    cache,
                    budget,
                    cancellationToken,
                    () => SaveCache(cache)
                )
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (YouTubeListQuota.IsExhausted(exception))
        {
            DateTimeOffset resume = YouTubeListQuota.ResumeAt(Clock());
            units.PauseLibrary(resume, Clock());
            logger.LogWarning(
                "YouTube quota is exhausted. The library pass waits until {ResumeAt:o}. Uploads continue.",
                resume
            );
        }
        finally
        {
            index.Save(indexPath);
        }

        logger.LogInformation(
            "YouTube library pass: {New} new channel video(s), {Recorded} recorded, {Unresolved} unresolved, {Filed} of {Planned} playlist inserts, {Units} units.",
            pass.NewVideos,
            pass.Recorded,
            pass.Unresolved,
            pass.Filed,
            pass.Planned.Count,
            pass.UnitsSpent
        );
        return pass;
    }

    /// <summary>
    /// A scheduled upload can fall past the uploads page the pass reads before it goes public.
    /// Recorded videos whose publish time has passed (within <see cref="ScheduledLookback"/>) and
    /// are not public yet get their privacy by id, 50 per call, so they are filed wherever they sit.
    /// </summary>
    internal static readonly TimeSpan ScheduledLookback = TimeSpan.FromDays(30);

    private async Task RefreshScheduledAsync(
        Dictionary<string, YouTubeLibraryVideo> record,
        string recordPath,
        Budget budget,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        List<YouTubeLibraryVideo> due = record
            .Values.Where(video =>
                video.PublishAt is DateTimeOffset at
                && at <= now
                && at >= now - ScheduledLookback
                && !string.Equals(video.PrivacyStatus, "public", StringComparison.Ordinal)
            )
            .OrderBy(video => video.PublishAt)
            .ToList();
        foreach (YouTubeLibraryVideo[] batch in due.Chunk(50))
        {
            if (!budget.TrySpend(YouTubeQuotaUnits.List))
            {
                return;
            }

            IReadOnlyDictionary<string, string> privacy = await playlists
                .PrivacyAsync(batch.Select(video => video.VideoId).ToList(), cancellationToken)
                .ConfigureAwait(false);
            foreach (YouTubeLibraryVideo video in batch)
            {
                if (
                    privacy.TryGetValue(video.VideoId, out string status)
                    && !string.IsNullOrWhiteSpace(status)
                    && !string.Equals(video.PrivacyStatus, status, StringComparison.Ordinal)
                )
                {
                    video.PrivacyStatus = status;
                    YouTubeLibraryRecord.Append(recordPath, video);
                }
            }
        }
    }

    private async Task ListUploadsAsync(
        YouTubeUploadsIndex index,
        Dictionary<string, YouTubeLibraryVideo> record,
        string recordPath,
        Budget budget,
        YouTubeLibraryPass pass,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(index.PlaylistId))
        {
            if (!budget.TrySpend(YouTubeQuotaUnits.List))
            {
                return;
            }

            index.PlaylistId = await playlists
                .UploadsPlaylistIdAsync(cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(index.PlaylistId))
            {
                logger.LogWarning(
                    "YouTube channel {Channel} has no uploads playlist.",
                    settings.YouTube?.ChannelId
                );
                return;
            }
        }

        // A new fact (the draft note, the named player) is read from every page once, so the
        // videos already in the record pick it up. That costs one unit per 50 videos.
        if (index.FactsVersion < YouTubeVideoFacts.Version)
        {
            index.ListedToEnd = false;
        }

        string catalog = YouTubeReplayCatalog.PathFor(settings.Location?.DataDirectory);
        var seen = new HashSet<string>(index.VideoIds, StringComparer.Ordinal);
        var waiting = index.Unresolved.ToDictionary(
            item => item.Video.VideoId,
            StringComparer.Ordinal
        );
        string pageToken = null;
        do
        {
            if (!budget.TrySpend(YouTubeQuotaUnits.List))
            {
                break;
            }

            YouTubeUploadsPage page = await playlists
                .UploadsAsync(index.PlaylistId, pageToken, cancellationToken)
                .ConfigureAwait(false);
            int newOnPage = 0;
            foreach (YouTubeUploadedVideo video in page?.Videos ?? [])
            {
                if (string.IsNullOrWhiteSpace(video?.VideoId))
                {
                    continue;
                }

                string videoId = video.VideoId.Trim();
                if (seen.Add(videoId))
                {
                    newOnPage++;
                }

                foreach (int id in YouTubeReplayMatch.IdsIn(video.Title, video.Description))
                {
                    YouTubeReplayCatalog.Remember(catalog, id);
                }

                Learn(video, videoId, record, recordPath, waiting, index, pass, now);
            }

            pass.NewVideos += newOnPage;
            pageToken = page?.NextPageToken;
            if (string.IsNullOrEmpty(pageToken))
            {
                index.ListedToEnd = true;
                index.FactsVersion = YouTubeVideoFacts.Version;
            }
            else if (newOnPage == 0 && index.ListedToEnd)
            {
                pageToken = null;
            }
        } while (!string.IsNullOrEmpty(pageToken));

        index.VideoIds = seen.ToList();
    }

    private static void Learn(
        YouTubeUploadedVideo video,
        string videoId,
        Dictionary<string, YouTubeLibraryVideo> record,
        string recordPath,
        Dictionary<string, YouTubeUnresolvedVideo> waiting,
        YouTubeUploadsIndex index,
        YouTubeLibraryPass pass,
        DateTimeOffset now
    )
    {
        string privacy = video.PrivacyStatus;
        YouTubeLibraryVideo facts = YouTubeVideoFacts.Read(
            videoId,
            video.Title,
            video.Description,
            privacy
        );
        if (record.TryGetValue(videoId, out YouTubeLibraryVideo known))
        {
            // A scheduled upload is recorded private and becomes public later.
            bool changed =
                !string.IsNullOrWhiteSpace(privacy)
                && !string.Equals(known.PrivacyStatus, privacy, StringComparison.Ordinal);
            if (changed)
            {
                known.PrivacyStatus = privacy;
            }

            // A line written before the record kept the draft note or the named player.
            if (YouTubeVideoFacts.Learn(known, facts) || changed)
            {
                YouTubeLibraryRecord.Append(recordPath, known);
            }

            return;
        }

        if (waiting.TryGetValue(videoId, out YouTubeUnresolvedVideo pending))
        {
            if (!string.IsNullOrWhiteSpace(privacy))
            {
                pending.Video.PrivacyStatus = privacy;
            }

            YouTubeVideoFacts.Learn(pending.Video, facts);
            return;
        }

        if (facts.IsResolved)
        {
            facts.UploadedAt = video.PublishedAt;
            YouTubeLibraryRecord.Append(recordPath, facts);
            record[videoId] = facts;
            pass.Recorded++;
            return;
        }

        facts.UploadedAt = video.PublishedAt;
        var unresolved = new YouTubeUnresolvedVideo { Video = facts, NextAttemptAt = now };
        index.Unresolved.Add(unresolved);
        waiting[videoId] = unresolved;
    }

    private async Task ResolveAsync(
        YouTubeUploadsIndex index,
        Dictionary<string, YouTubeLibraryVideo> record,
        string recordPath,
        YouTubeLibraryPass pass,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var waiting = new HashSet<string>(
            index.Unresolved.Select(item => item.Video.VideoId),
            StringComparer.Ordinal
        );
        foreach (YouTubeLibraryVideo video in record.Values)
        {
            // An inserted clip has no build. Heroes Profile has it.
            if (!video.IsResolved && video.ReplayId is > 0 && waiting.Add(video.VideoId))
            {
                index.Unresolved.Add(
                    new YouTubeUnresolvedVideo
                    {
                        Video = JsonSerializer.Deserialize<YouTubeLibraryVideo>(
                            JsonSerializer.Serialize(video)
                        ),
                        NextAttemptAt = now,
                    }
                );
            }
        }

        int limit = Math.Max(0, settings.YouTube?.LibraryLookupsPerPass ?? 0);
        int lookups = 0;
        foreach (YouTubeUnresolvedVideo item in index.Unresolved.ToList())
        {
            if (item.NextAttemptAt > now)
            {
                continue;
            }

            if (item.Video.ReplayId is not int replayId || replayId <= 0)
            {
                // No replay id in the title or description. Nothing can resolve it.
                item.NextAttemptAt = DateTimeOffset.MaxValue;
                continue;
            }

            if (heroesProfile == null || lookups >= limit)
            {
                break;
            }

            lookups++;
            try
            {
                HeroesProfileReplay replay = await heroesProfile
                    .GetReplayByIdAsync(replayId)
                    .ConfigureAwait(false);
                YouTubeVideoFacts.Fill(item.Video, replay);
                if (replay != null && YouTubeVideoFacts.NeedsRank(item.Video))
                {
                    await heroesProfile
                        .EnrichRankAsync(replay, cancellationToken)
                        .ConfigureAwait(false);
                    YouTubeVideoFacts.Fill(item.Video, replay);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not look up replay {ReplayId} for YouTube video {VideoId}.",
                    replayId,
                    item.Video.VideoId
                );
            }

            if (item.Video.IsResolved)
            {
                YouTubeLibraryRecord.Append(recordPath, item.Video);
                record[item.Video.VideoId] = item.Video;
                index.Unresolved.Remove(item);
                pass.Recorded++;
                continue;
            }

            item.Attempts++;
            item.NextAttemptAt = now + YouTubeUploadsIndex.RetryAfter(item.Attempts);
            logger.LogInformation(
                "YouTube video {VideoId} (replay {ReplayId}) is not resolved. Next try {NextAttemptAt:o}.",
                item.Video.VideoId,
                replayId,
                item.NextAttemptAt
            );
        }

        pass.Unresolved = index.Unresolved.Count;
    }

    /// <summary>
    /// The record's resolved public videos, then each uploaded context entry the record does
    /// not have yet.
    /// </summary>
    private IReadOnlyList<YouTubeLibraryItem> Plan(
        IReadOnlyDictionary<string, YouTubeLibraryVideo> record
    )
    {
        var videos = record.Values.Where(video => video.IsResolved).ToList();
        foreach (YouTubeEntry entry in ReadUploadedEntries())
        {
            if (
                !string.IsNullOrWhiteSpace(entry.VideoId)
                && !record.ContainsKey(entry.VideoId.Trim())
            )
            {
                videos.Add(YouTubeLibraryRecord.FromEntry(entry, uploadedAt: null));
            }
        }

        return YouTubeLibraryPlanner.Plan(
            videos,
            settings.YouTube?.Playlists,
            settings.Spectate?.MinimumGameVersion,
            settings.YouTube?.SeasonName
        );
    }

    private static IReadOnlyList<YouTubeLibraryItem> Pending(
        IReadOnlyList<YouTubeLibraryItem> items,
        YouTubePlaylistCache cache
    )
    {
        var filed = new HashSet<string>(cache.FiledVideoIds ?? [], StringComparer.Ordinal);
        return items.Where(item => !filed.Contains(FiledKey(item))).ToList();
    }

    private async Task<int> FileAsync(
        IReadOnlyList<YouTubeLibraryItem> items,
        YouTubePlaylistCache cache,
        Budget budget,
        CancellationToken cancellationToken,
        Action persist
    )
    {
        cache.PlaylistIds ??= new Dictionary<string, string>(StringComparer.Ordinal);
        cache.FiledVideoIds ??= new List<string>();
        var filed = new HashSet<string>(cache.FiledVideoIds, StringComparer.Ordinal);
        bool listed = false;
        int failures = 0;
        int count = 0;
        foreach (YouTubeLibraryItem item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string filedKey = FiledKey(item);
            if (string.IsNullOrWhiteSpace(item?.VideoId) || filed.Contains(filedKey))
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
                    if (!listed)
                    {
                        listed = true;
                        if (
                            !await ListPlaylistsAsync(cache, budget, cancellationToken)
                                .ConfigureAwait(false)
                        )
                        {
                            break;
                        }

                        persist();
                    }

                    if (
                        !cache.PlaylistIds.TryGetValue(item.PlaylistTitle, out playlistId)
                        || string.IsNullOrWhiteSpace(playlistId)
                    )
                    {
                        if (!budget.TrySpend(YouTubeQuotaUnits.PlaylistInsert))
                        {
                            break;
                        }

                        playlistId = await playlists
                            .CreateAsync(item.PlaylistTitle, cancellationToken)
                            .ConfigureAwait(false);
                        cache.PlaylistIds[item.PlaylistTitle] = playlistId;
                        persist();
                    }
                }

                if (!budget.TrySpend(YouTubeQuotaUnits.PlaylistItemInsert))
                {
                    break;
                }

                await playlists
                    .InsertAsync(playlistId, item.VideoId.Trim(), cancellationToken)
                    .ConfigureAwait(false);
                filed.Add(filedKey);
                cache.FiledVideoIds.Add(filedKey);
                count++;
                failures = 0;
                persist();
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
            catch (Exception exception) when (!YouTubeListQuota.IsExhausted(exception))
            {
                failures++;
                logger.LogWarning(
                    exception,
                    "Could not file {VideoId} into {Playlist}.",
                    item.VideoId,
                    item.PlaylistTitle
                );
                if (failures >= FailuresBeforeStop)
                {
                    logger.LogWarning(
                        "YouTube library filing stopped after {Failures} failures in a row.",
                        failures
                    );
                    break;
                }
            }
        }

        if (budget.Spent)
        {
            logger.LogInformation(
                "YouTube library pass used its units for today. {Left} playlist insert(s) wait for a later pass.",
                items.Count - count
            );
        }

        return count;
    }

    private async Task<bool> ListPlaylistsAsync(
        YouTubePlaylistCache cache,
        Budget budget,
        CancellationToken cancellationToken
    )
    {
        string pageToken = null;
        do
        {
            if (!budget.TrySpend(YouTubeQuotaUnits.List))
            {
                return false;
            }

            YouTubePlaylistsPage page = await playlists
                .PlaylistsAsync(pageToken, cancellationToken)
                .ConfigureAwait(false);
            foreach (YouTubePlaylist playlist in page?.Playlists ?? [])
            {
                if (
                    !string.IsNullOrWhiteSpace(playlist?.Title)
                    && !string.IsNullOrWhiteSpace(playlist.Id)
                )
                {
                    cache.PlaylistIds.TryAdd(playlist.Title, playlist.Id);
                }
            }

            pageToken = page?.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken));

        return true;
    }

    private async Task<YouTubeLibraryPass> DryRunAsync(
        string data,
        YouTubeQuotaUnits units,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        Dictionary<string, YouTubeLibraryVideo> record = YouTubeLibraryRecord.Read(
            YouTubeLibraryRecord.PathFor(data)
        );
        YouTubeUploadsIndex index = YouTubeUploadsIndex.Load(YouTubeUploadsIndex.PathFor(data));
        IReadOnlyList<YouTubeLibraryItem> items = Pending(Plan(record), LoadCache());
        YouTubeQuotaDay day = units.Read(now);
        var due = index.Unresolved.Where(item => item.NextAttemptAt <= now).ToList();
        string json = JsonSerializer.Serialize(
            new
            {
                Simulated = true,
                Recorded = record.Count,
                Planned = items.Count,
                InsertUnits = items.Count * YouTubeQuotaUnits.PlaylistItemInsert,
                Playlists = items
                    .GroupBy(item => item.PlaylistTitle, StringComparer.Ordinal)
                    .Select(group => new { Title = group.Key, Videos = group.Count() }),
                Items = items,
                Discovery = new
                {
                    WouldList = true,
                    UploadsPlaylistKnown = !string.IsNullOrWhiteSpace(index.PlaylistId),
                    index.ListedToEnd,
                    KnownVideos = index.VideoIds.Count,
                    Unresolved = index.Unresolved.Count,
                    HeroesProfileLookups = Math.Min(
                        due.Count,
                        Math.Max(0, settings.YouTube?.LibraryLookupsPerPass ?? 0)
                    ),
                    WouldResolve = due.Select(item => new
                    {
                        item.Video.VideoId,
                        item.Video.ReplayId,
                    }),
                },
                Units = new
                {
                    day.QuotaDay,
                    day.UploadUnits,
                    day.LibraryUnits,
                    LibraryRoom = units.LibraryRoom(day),
                },
            },
            new JsonSerializerOptions { WriteIndented = true }
        );
        await File.WriteAllTextAsync(Path.Combine(data, DryRunFileName), json, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation(
            "YouTube library dry-run planned {Count} playlist inserts from {Recorded} recorded videos. YouTube was not called.",
            items.Count,
            record.Count
        );
        return new YouTubeLibraryPass
        {
            DryRun = true,
            Planned = items,
            Unresolved = index.Unresolved.Count,
        };
    }

    private List<YouTubeEntry> ReadUploadedEntries()
    {
        var entries = new List<YouTubeEntry>();
        string contexts = settings.ContextsDirectory;
        if (string.IsNullOrWhiteSpace(contexts) || !Directory.Exists(contexts))
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

    private YouTubePlaylistCache LoadCache()
    {
        string json = DurableFile.ReadOrAside(CachePath());
        if (string.IsNullOrWhiteSpace(json))
        {
            return new YouTubePlaylistCache();
        }

        try
        {
            return JsonSerializer.Deserialize<YouTubePlaylistCache>(json)
                ?? new YouTubePlaylistCache();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Ignoring unreadable playlist cache {Path}.", CachePath());
            DurableFile.Aside(CachePath());
            return new YouTubePlaylistCache();
        }
    }

    private void SaveCache(YouTubePlaylistCache cache) =>
        DurableFile.Replace(
            CachePath(),
            JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true })
        );

    private string CachePath()
    {
        string directory = settings.Location?.DataDirectory;
        return string.IsNullOrWhiteSpace(directory) ? null : Path.Combine(directory, CacheFileName);
    }

    private static string FiledKey(YouTubeLibraryItem item) =>
        (item?.PlaylistTitle ?? string.Empty) + "\n" + (item?.VideoId?.Trim() ?? string.Empty);

    /// <summary>
    /// Reserves units in the shared ledger and counts them on the pass. Once a reservation
    /// fails, every later one in the pass fails too.
    /// </summary>
    private sealed class Budget(
        YouTubeQuotaUnits units,
        Func<DateTimeOffset> clock,
        YouTubeLibraryPass pass
    )
    {
        public bool Spent { get; private set; }

        public bool TrySpend(int cost)
        {
            if (Spent || !units.TrySpendLibrary(cost, clock()))
            {
                Spent = true;
                return false;
            }

            pass.UnitsSpent += cost;
            return true;
        }
    }
}
