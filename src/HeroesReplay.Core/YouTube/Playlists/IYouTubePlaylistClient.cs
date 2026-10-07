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

/// <summary>
/// A video's <c>status</c> part: privacy, the scheduled publish time, and why YouTube did not
/// process or keep it (<c>uploadStatus</c> rejected or failed, <c>rejectionReason</c>,
/// <c>failureReason</c>).
/// </summary>
public sealed record YouTubeVideoStatus(
    string PrivacyStatus,
    string UploadStatus = null,
    string RejectionReason = null,
    string FailureReason = null,
    DateTimeOffset? PublishAt = null
);

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
    /// <summary>
    /// True when the library consent (<c>{ChannelId}:library</c>, the full youtube scope) is
    /// stored with a refresh token, so a call can run without asking anyone to sign in.
    /// </summary>
    Task<bool> HasConsentAsync(CancellationToken cancellationToken);

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

    /// <summary>
    /// <c>status</c> by video id for up to 50 ids (one videos.list call). Missing ids are left out.
    /// </summary>
    Task<IReadOnlyDictionary<string, YouTubeVideoStatus>> StatusAsync(
        IReadOnlyList<string> videoIds,
        CancellationToken cancellationToken
    );

    Task<string> CreateAsync(string title, CancellationToken cancellationToken);

    Task InsertAsync(string playlistId, string videoId, CancellationToken cancellationToken);
}
