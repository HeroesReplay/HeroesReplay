namespace HeroesReplay.Core.YouTube.Playlists;

/// <summary>
/// Which playlist groups the library pass files a public video into (<c>YouTube:Playlists</c>).
/// Each group that applies to a video costs one <c>playlistItems.insert</c> (50 units).
/// Turning a group off stops new inserts. Nothing already filed is removed.
/// </summary>
public class YouTubePlaylistSettings
{
    /// <summary>One playlist per map, every mode: <c>Alterac Pass</c>.</summary>
    public bool Map { get; set; } = true;

    /// <summary>One playlist per mode: <c>Storm League</c>, <c>Quick Match</c>, <c>ARAM</c>.</summary>
    public bool Mode { get; set; } = true;

    /// <summary>One playlist per Storm League tier: <c>Storm League - Diamond</c>.</summary>
    public bool Rank { get; set; } = true;

    /// <summary>One playlist per draft note: <c>Unusual drafts - Double healer</c>.</summary>
    public bool Draft { get; set; } = true;

    /// <summary>
    /// One playlist for a viewer request that named a player: <c>Viewer requested reviews</c>.
    /// </summary>
    public bool ViewerReview { get; set; } = true;

    /// <summary>
    /// The current patch line (or <c>YouTube:SeasonName</c>), then <c>Patch 2.55 archive</c>.
    /// Clips are filed here only.
    /// </summary>
    public bool Patch { get; set; } = true;

    /// <summary>
    /// The earlier combined playlist: <c>Alterac Pass - Storm League - Diamond</c>. Off by
    /// default, because it is one playlist per map and tier and repeats the map and rank groups.
    /// </summary>
    public bool MapMode { get; set; }
}
