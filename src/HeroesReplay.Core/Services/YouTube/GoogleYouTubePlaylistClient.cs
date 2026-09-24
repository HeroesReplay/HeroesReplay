using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.YouTube;

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

    public async Task<string> FindOrCreateAsync(string title, CancellationToken cancellationToken)
    {
        YouTubeService youtube = await ServiceAsync(cancellationToken).ConfigureAwait(false);
        string page = null;
        do
        {
            PlaylistsResource.ListRequest list = youtube.Playlists.List("snippet");
            list.Mine = true;
            list.MaxResults = 50;
            list.PageToken = page;
            PlaylistListResponse response = await list.ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
            if (response.Items != null)
            {
                foreach (Playlist playlist in response.Items)
                {
                    if (string.Equals(playlist.Snippet?.Title, title, StringComparison.Ordinal))
                    {
                        return playlist.Id;
                    }
                }
            }

            page = response.NextPageToken;
        } while (!string.IsNullOrEmpty(page));

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
        string user = string.IsNullOrWhiteSpace(settings.YouTube?.ChannelId)
            ? "heroesreplay-library"
            : settings.YouTube.ChannelId + ":library";
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
