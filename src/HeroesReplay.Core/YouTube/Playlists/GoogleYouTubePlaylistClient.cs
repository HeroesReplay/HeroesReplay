using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.YouTube.Playlists;

public sealed class GoogleYouTubePlaylistClient : IYouTubePlaylistClient
{
    private readonly ILogger<GoogleYouTubePlaylistClient> logger;
    private readonly AppSettings settings;
    private YouTubeService service;

    public GoogleYouTubePlaylistClient(
        ILogger<GoogleYouTubePlaylistClient> logger,
        AppSettings settings
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Reads the token store the authorization broker uses, without the broker: the broker opens
    /// a browser and waits for a sign-in when the consent is missing, which would hang a
    /// background pass on the stream PC.
    /// </summary>
    public async Task<bool> HasConsentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            TokenResponse token = await new FileDataStore(GoogleWebAuthorizationBroker.Folder)
                .GetAsync<TokenResponse>(YouTubeLibrary.LibraryUser(settings))
                .ConfigureAwait(false);
            return !string.IsNullOrWhiteSpace(token?.RefreshToken);
        }
        catch (Exception exception)
            when (exception is IOException
                || exception is UnauthorizedAccessException
                || exception is Newtonsoft.Json.JsonException
            )
        {
            logger.LogWarning(exception, "Could not read the stored YouTube library consent.");
            return false;
        }
    }

    public async Task<string> UploadsPlaylistIdAsync(CancellationToken cancellationToken)
    {
        YouTubeService youtube = await ServiceAsync(cancellationToken).ConfigureAwait(false);
        ChannelsResource.ListRequest request = youtube.Channels.List("contentDetails");
        if (string.IsNullOrWhiteSpace(settings.YouTube?.ChannelId))
        {
            request.Mine = true;
        }
        else
        {
            request.Id = settings.YouTube.ChannelId;
        }

        ChannelListResponse response = await request
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
        return response?.Items?.FirstOrDefault()?.ContentDetails?.RelatedPlaylists?.Uploads;
    }

    public async Task<YouTubeUploadsPage> UploadsAsync(
        string playlistId,
        string pageToken,
        CancellationToken cancellationToken
    )
    {
        YouTubeService youtube = await ServiceAsync(cancellationToken).ConfigureAwait(false);
        PlaylistItemsResource.ListRequest request = youtube.PlaylistItems.List("snippet,status");
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
                    PrivacyStatus = item.Status?.PrivacyStatus,
                    PublishedAt = item.Snippet?.PublishedAtDateTimeOffset,
                })
                .ToList(),
        };
    }

    public async Task<IReadOnlyDictionary<string, YouTubeVideoStatus>> StatusAsync(
        IReadOnlyList<string> videoIds,
        CancellationToken cancellationToken
    )
    {
        var privacy = new Dictionary<string, YouTubeVideoStatus>(StringComparer.Ordinal);
        if (videoIds == null || videoIds.Count == 0)
        {
            return privacy;
        }

        YouTubeService youtube = await ServiceAsync(cancellationToken).ConfigureAwait(false);
        VideosResource.ListRequest request = youtube.Videos.List("status");
        request.Id = string.Join(",", videoIds.Take(50));
        request.MaxResults = 50;
        VideoListResponse response = await request
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (Video video in response?.Items ?? Array.Empty<Video>())
        {
            if (!string.IsNullOrWhiteSpace(video?.Id))
            {
                privacy[video.Id] = new YouTubeVideoStatus(
                    video.Status?.PrivacyStatus,
                    video.Status?.UploadStatus,
                    video.Status?.RejectionReason,
                    video.Status?.FailureReason,
                    video.Status?.PublishAtDateTimeOffset
                );
            }
        }

        return privacy;
    }

    public async Task<YouTubePlaylistsPage> PlaylistsAsync(
        string pageToken,
        CancellationToken cancellationToken
    )
    {
        YouTubeService youtube = await ServiceAsync(cancellationToken).ConfigureAwait(false);
        PlaylistsResource.ListRequest list = youtube.Playlists.List("snippet");
        list.Mine = true;
        list.MaxResults = 50;
        list.PageToken = pageToken;
        PlaylistListResponse response = await list.ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
        return new YouTubePlaylistsPage
        {
            NextPageToken = response?.NextPageToken,
            Playlists = (response?.Items ?? Array.Empty<Playlist>())
                .Select(playlist => new YouTubePlaylist(playlist.Id, playlist.Snippet?.Title))
                .ToList(),
        };
    }

    public async Task<string> CreateAsync(string title, CancellationToken cancellationToken)
    {
        YouTubeService youtube = await ServiceAsync(cancellationToken).ConfigureAwait(false);
        Playlist created = await youtube
            .Playlists.Insert(
                new Playlist
                {
                    Snippet = new PlaylistSnippet { Title = title },
                    Status = new PlaylistStatus { PrivacyStatus = "public" },
                },
                "snippet,status"
            )
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation(
            "Created YouTube playlist {Title} ({PlaylistId}).",
            title,
            created.Id
        );
        return created.Id;
    }

    public async Task InsertAsync(
        string playlistId,
        string videoId,
        CancellationToken cancellationToken
    )
    {
        YouTubeService youtube = await ServiceAsync(cancellationToken).ConfigureAwait(false);
        var item = new PlaylistItem
        {
            Snippet = new PlaylistItemSnippet
            {
                PlaylistId = playlistId,
                ResourceId = new ResourceId { Kind = "youtube#video", VideoId = videoId },
            },
        };
        try
        {
            await youtube
                .PlaylistItems.Insert(item, "snippet")
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Google.GoogleApiException exception)
            when (exception.HttpStatusCode == HttpStatusCode.Conflict)
        {
            logger.LogInformation(
                "Video {VideoId} is already in playlist {PlaylistId}.",
                videoId,
                playlistId
            );
        }
    }

    private async Task<YouTubeService> ServiceAsync(CancellationToken cancellationToken)
    {
        if (service != null)
        {
            return service;
        }

        string secretsPath = Path.Combine(settings.Location.DataDirectory, "client_secrets.json");
        if (!File.Exists(secretsPath))
        {
            throw new FileNotFoundException(
                "Google OAuth client_secrets.json is missing. Run tools/fill-secrets-from-op.ps1.",
                secretsPath
            );
        }

        await using FileStream stream = new(secretsPath, FileMode.Open, FileAccess.Read);
        string user = YouTubeLibrary.LibraryUser(settings);
        UserCredential credential = await GoogleWebAuthorizationBroker
            .AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                new[] { YouTubeService.Scope.Youtube },
                user,
                cancellationToken
            )
            .ConfigureAwait(false);
        service = new YouTubeService(
            new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApiKey = settings.YouTube?.ApiKey,
                ApplicationName = Assembly.GetExecutingAssembly().GetName().Name,
            }
        );
        logger.LogInformation("YouTube playlist OAuth ready for {User}.", user);
        return service;
    }
}
