using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.YouTube.Playlists;

public sealed class YouTubeUploadedVideo
{
    public string VideoId { get; init; }
    public string Title { get; init; }
    public string Description { get; init; }
    public string PrivacyStatus { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
}

public sealed class YouTubeUploadsPage
{
    public IReadOnlyList<YouTubeUploadedVideo> Videos { get; init; }
    public string NextPageToken { get; init; }
}

public sealed record YouTubePlaylist(string Id, string Title);

public sealed class YouTubePlaylistsPage
{
    public IReadOnlyList<YouTubePlaylist> Playlists { get; init; }
    public string NextPageToken { get; init; }
}

/// <summary>
/// The channel calls the library pass makes. Each list call costs one quota unit, and each
/// insert costs 50. The caller reserves those units before it calls.
/// </summary>
public interface IYouTubePlaylistClient
{
    Task<string> UploadsPlaylistIdAsync(CancellationToken cancellationToken);

    Task<YouTubeUploadsPage> UploadsAsync(
        string playlistId,
        string pageToken,
        CancellationToken cancellationToken
    );

    Task<YouTubePlaylistsPage> PlaylistsAsync(
        string pageToken,
        CancellationToken cancellationToken
    );

    /// <summary>Privacy status by video id for up to 50 ids (one videos.list call). Missing ids are left out.</summary>
    Task<IReadOnlyDictionary<string, string>> PrivacyAsync(
        IReadOnlyList<string> videoIds,
        CancellationToken cancellationToken
    );

    Task<string> CreateAsync(string title, CancellationToken cancellationToken);

    Task InsertAsync(string playlistId, string videoId, CancellationToken cancellationToken);
}
