using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.YouTube;

public sealed class YouTubeApiVideoSearch : IYouTubeVideoSearch
{
    private readonly ILogger<YouTubeApiVideoSearch> logger;
    private readonly AppSettings settings;

    public YouTubeApiVideoSearch(ILogger<YouTubeApiVideoSearch> logger, AppSettings settings)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<IReadOnlyList<YouTubeVideoText>> SearchAsync(
        int replayId,
        CancellationToken cancellationToken
    )
    {
        string apiKey = settings.YouTube?.ApiKey;
        string channelId = settings.YouTube?.ChannelId;
        if (
            replayId <= 0
            || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(channelId)
        )
        {
            logger.LogWarning(
                "YouTube search skipped for replay {ReplayId}. ApiKey or ChannelId is missing.",
                replayId
            );
            return Array.Empty<YouTubeVideoText>();
        }

        using var youtube = new YouTubeService(
            new BaseClientService.Initializer { ApiKey = apiKey, ApplicationName = "HeroesReplay" }
        );
        var request = youtube.Search.List("snippet");
        request.ChannelId = channelId;
        request.Q = replayId.ToString();
        request.Type = "video";
        request.MaxResults = 5;
        var response = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (response?.Items == null)
        {
            return Array.Empty<YouTubeVideoText>();
        }

        return response
            .Items.Select(item => new YouTubeVideoText
            {
                Title = item.Snippet?.Title,
                Description = item.Snippet?.Description,
            })
            .ToList();
    }
}
