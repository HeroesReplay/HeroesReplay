using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.YouTube;

public sealed class YouTubeReplayLookup : IYouTubeReplayLookup
{
    private readonly ILogger<YouTubeReplayLookup> logger;
    private readonly AppSettings settings;
    private readonly IYouTubeVideoSearch search;

    public YouTubeReplayLookup(
        ILogger<YouTubeReplayLookup> logger,
        AppSettings settings,
        IYouTubeVideoSearch search
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.search = search ?? throw new ArgumentNullException(nameof(search));
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

        try
        {
            foreach (
                YouTubeVideoText video in await search.SearchAsync(
                    replayId.Value,
                    cancellationToken
                )
            )
            {
                if (
                    YouTubeReplayMatch.Mentions(video?.Title, replayId.Value)
                    || YouTubeReplayMatch.Mentions(video?.Description, replayId.Value)
                )
                {
                    YouTubeReplayCatalog.Remember(catalog, replayId.Value);
                    logger.LogInformation(
                        "YouTube already has replay {ReplayId} ({Title}).",
                        replayId.Value,
                        video?.Title
                    );
                    return true;
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(
                e,
                "Could not search YouTube for replay {ReplayId}. Recording stays on.",
                replayId.Value
            );
            return false;
        }

        return false;
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
