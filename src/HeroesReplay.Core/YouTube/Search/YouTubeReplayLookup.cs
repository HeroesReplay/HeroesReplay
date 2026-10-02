using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Replays;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.YouTube.Search;

/// <summary>
/// A replay is already on YouTube when the catalog or an upload receipt names it, or when
/// the channel's uploads playlist does. That playlist is listed at most once per
/// <c>YouTube:UploadsIndexRefresh</c>, and only until a page has no new video.
/// </summary>
public sealed class YouTubeReplayLookup : IYouTubeReplayLookup
{
    private readonly ILogger<YouTubeReplayLookup> logger;
    private readonly AppSettings settings;
    private readonly IYouTubeUploadsListing listing;
    private DateTimeOffset listingPausedUntil;

    public YouTubeReplayLookup(
        ILogger<YouTubeReplayLookup> logger,
        AppSettings settings,
        IYouTubeUploadsListing listing
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.listing = listing ?? throw new ArgumentNullException(nameof(listing));
    }

    public async Task<bool> AlreadyUploadedAsync(
        LoadedReplay replay,
        CancellationToken cancellationToken
    )
    {
        int? replayId = replay?.HeroesProfileReplay?.Id ?? replay?.ReplayId;
        if (replayId is not > 0)
        {
            return false;
        }

        string catalog = YouTubeReplayCatalog.PathFor(settings.Location?.DataDirectory);
        if (YouTubeReplayCatalog.Contains(catalog, replayId.Value))
        {
            return true;
        }

        if (HasUploadReceipt(replayId.Value))
        {
            YouTubeReplayCatalog.Remember(catalog, replayId.Value);
            return true;
        }

        if (settings.YouTube?.DryRun != false || DateTimeOffset.UtcNow < listingPausedUntil)
        {
            return false;
        }

        string indexPath = YouTubeUploadsIndex.PathFor(settings.Location?.DataDirectory);
        YouTubeUploadsIndex index = YouTubeUploadsIndex.Load(indexPath);
        if (index.IsFresh(DateTimeOffset.UtcNow, settings.YouTube.UploadsIndexRefresh))
        {
            return false;
        }

        try
        {
            await RefreshAsync(index, indexPath, catalog, replayId.Value, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (YouTubeListQuota.IsExhausted(e))
            {
                listingPausedUntil = YouTubeListQuota.ResumeAt(DateTimeOffset.UtcNow);
                logger.LogWarning(
                    "YouTube uploads listing quota is exhausted. Duplicate checks use the local catalog until {ResumeAt:o}. Recording stays on.",
                    listingPausedUntil
                );
                return false;
            }

            logger.LogWarning(
                e,
                "Could not list YouTube uploads for replay {ReplayId}. Recording stays on.",
                replayId.Value
            );
            return false;
        }

        if (YouTubeReplayCatalog.Contains(catalog, replayId.Value))
        {
            logger.LogInformation("YouTube already has replay {ReplayId}.", replayId.Value);
            return true;
        }

        return false;
    }

    private async Task RefreshAsync(
        YouTubeUploadsIndex index,
        string indexPath,
        string catalog,
        int replayId,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(index.PlaylistId))
        {
            index.PlaylistId = await listing
                .UploadsPlaylistIdAsync(cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(index.PlaylistId))
            {
                return;
            }
        }

        var known = new HashSet<string>(index.VideoIds, StringComparer.Ordinal);
        int pages = 0;
        int added = 0;
        string pageToken = null;
        do
        {
            YouTubeUploadsPage page = await listing
                .PageAsync(index.PlaylistId, pageToken, cancellationToken)
                .ConfigureAwait(false);
            pages++;
            int newOnPage = 0;
            foreach (YouTubeUploadedVideo video in page?.Videos ?? [])
            {
                if (string.IsNullOrEmpty(video?.VideoId) || !known.Add(video.VideoId))
                {
                    continue;
                }

                newOnPage++;
                foreach (int id in YouTubeReplayMatch.IdsIn(video.Title, video.Description))
                {
                    YouTubeReplayCatalog.Remember(catalog, id);
                }

                // An older title or description can name the replay in a form IdsIn does not read.
                if (
                    YouTubeReplayMatch.Mentions(video.Title, replayId)
                    || YouTubeReplayMatch.Mentions(video.Description, replayId)
                )
                {
                    YouTubeReplayCatalog.Remember(catalog, replayId);
                }
            }

            added += newOnPage;
            pageToken = newOnPage == 0 ? null : page?.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken));

        index.VideoIds = known.ToList();
        index.RefreshedAt = DateTimeOffset.UtcNow;
        index.Save(indexPath);
        logger.LogInformation(
            "YouTube uploads index refreshed: {Pages} page(s), {Added} new video(s), {Known} known.",
            pages,
            added,
            known.Count
        );
    }

    private bool HasUploadReceipt(int replayId)
    {
        string uploadedName = settings.YouTube?.EntryFileNameUploaded;
        if (
            string.IsNullOrWhiteSpace(uploadedName)
            || string.IsNullOrWhiteSpace(settings.ContextsDirectory)
        )
        {
            return false;
        }

        string receipt = Path.Combine(
            settings.ContextsDirectory,
            replayId.ToString(),
            uploadedName
        );
        return File.Exists(receipt);
    }
}
