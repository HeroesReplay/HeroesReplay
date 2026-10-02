using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Replays;

namespace HeroesReplay.Core.YouTube.Search;

public sealed class YouTubeUploadedVideo
{
    public string VideoId { get; init; }
    public string Title { get; init; }
    public string Description { get; init; }
}

public sealed class YouTubeUploadsPage
{
    public IReadOnlyList<YouTubeUploadedVideo> Videos { get; init; }
    public string NextPageToken { get; init; }
}

/// <summary>
/// The channel's uploads playlist, newest first. Each call costs one quota unit.
/// </summary>
public interface IYouTubeUploadsListing
{
    Task<string> UploadsPlaylistIdAsync(CancellationToken cancellationToken);

    Task<YouTubeUploadsPage> PageAsync(
        string playlistId,
        string pageToken,
        CancellationToken cancellationToken
    );
}

public interface IYouTubeReplayLookup
{
    Task<bool> AlreadyUploadedAsync(LoadedReplay replay, CancellationToken cancellationToken);
}
