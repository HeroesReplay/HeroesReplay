using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.YouTube.Search;

/// <summary>
/// Lists the public uploads of <c>YouTube:ChannelId</c> with the API key. A private or
/// scheduled upload is not listed. The uploader already wrote that replay id to the catalog.
/// </summary>
public sealed class YouTubeApiUploadsListing : IYouTubeUploadsListing
{
    private readonly ILogger<YouTubeApiUploadsListing> logger;
    private readonly AppSettings settings;

    public YouTubeApiUploadsListing(ILogger<YouTubeApiUploadsListing> logger, AppSettings settings)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<string> UploadsPlaylistIdAsync(CancellationToken cancellationToken)
    {
        string channelId = settings.YouTube?.ChannelId;
        if (
            string.IsNullOrWhiteSpace(channelId)
            || string.IsNullOrWhiteSpace(settings.YouTube?.ApiKey)
        )
        {
            logger.LogWarning("YouTube uploads index skipped. ApiKey or ChannelId is missing.");
            return null;
        }

        using YouTubeService youtube = Service();
        ChannelsResource.ListRequest request = youtube.Channels.List("contentDetails");
        request.Id = channelId;
        ChannelListResponse response = await request
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
        return response?.Items?.FirstOrDefault()?.ContentDetails?.RelatedPlaylists?.Uploads;
    }

    public async Task<YouTubeUploadsPage> PageAsync(
        string playlistId,
        string pageToken,
        CancellationToken cancellationToken
    )
    {
        using YouTubeService youtube = Service();
        PlaylistItemsResource.ListRequest request = youtube.PlaylistItems.List("snippet");
        request.PlaylistId = playlistId;
        request.MaxResults = 50;
        request.PageToken = pageToken;
        PlaylistItemListResponse response = await request
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
        return new YouTubeUploadsPage
        {
            NextPageToken = response?.NextPageToken,
            Videos = (response?.Items ?? Array.Empty<PlaylistItem>())
                .Select(item => new YouTubeUploadedVideo
                {
                    VideoId = item.Snippet?.ResourceId?.VideoId,
                    Title = item.Snippet?.Title,
                    Description = item.Snippet?.Description,
                })
                .ToList(),
        };
    }

    private YouTubeService Service() =>
        new(
            new BaseClientService.Initializer
            {
                ApiKey = settings.YouTube?.ApiKey,
                ApplicationName = "HeroesReplay",
            }
        );
}
