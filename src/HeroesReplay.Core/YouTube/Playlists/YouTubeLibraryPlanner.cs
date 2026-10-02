using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.HeroesProfile;

namespace HeroesReplay.Core.YouTube.Playlists;

public sealed record YouTubeLibraryItem(string PlaylistTitle, string VideoId, int? ReplayId);

/// <summary>
/// The playlist inserts for the library pass. Each public video goes into one playlist per
/// group that is on in <c>YouTube:Playlists</c> and that its facts allow, and into each
/// playlist only once. The newest upload comes first, so a new video is not left behind
/// the backlog of older ones.
/// </summary>
public static class YouTubeLibraryPlanner
{
    public static IReadOnlyList<YouTubeLibraryItem> Plan(
        IEnumerable<YouTubeLibraryVideo> videos,
        YouTubePlaylistSettings groups,
        string currentPatchLine,
        string seasonName = null
    )
    {
        if (videos == null)
        {
            return Array.Empty<YouTubeLibraryItem>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<YouTubeLibraryItem>();
        foreach (
            YouTubeLibraryVideo video in videos
                .Where(video => video != null)
                .OrderByDescending(video => video.UploadedAt ?? DateTimeOffset.MinValue)
        )
        {
            if (
                string.IsNullOrWhiteSpace(video.VideoId)
                || !PatchPlaylist.MayFile(video.PrivacyStatus)
            )
            {
                continue;
            }

            string videoId = video.VideoId.Trim();
            if (!seen.Add(videoId))
            {
                continue;
            }

            foreach (string title in Titles(video, groups, currentPatchLine, seasonName))
            {
                items.Add(new YouTubeLibraryItem(title, videoId, video.ReplayId));
            }
        }

        return items;
    }

    /// <summary>
    /// The playlists one video belongs in, in group order, each title once. A full match needs
    /// a filed mode (Storm League, Quick Match, ARAM, or Unranked Draft) for every group except
    /// the patch. A clip goes into the patch playlist only.
    /// </summary>
    public static IReadOnlyList<string> Titles(
        YouTubeLibraryVideo video,
        YouTubePlaylistSettings groups,
        string currentPatchLine,
        string seasonName = null
    )
    {
        var titles = new List<string>();
        if (video == null)
        {
            return titles;
        }

        groups ??= new YouTubePlaylistSettings();
        if (!video.IsClip && YouTubePlaylistNames.Mode(video.Mode) != null)
        {
            if (groups.Map)
            {
                Add(YouTubePlaylistNames.Map(video.Map));
            }

            if (groups.Mode)
            {
                Add(YouTubePlaylistNames.Mode(video.Mode));
            }

            if (groups.Rank)
            {
                Add(YouTubePlaylistNames.Rank(video.Mode, video.Rank));
            }

            if (groups.Draft)
            {
                foreach (string draft in YouTubePlaylistNames.Drafts(video.Draft))
                {
                    Add(draft);
                }
            }

            if (groups.ViewerReview && video.IsViewerReview)
            {
                Add(YouTubePlaylistNames.ViewerReviews);
            }

            if (groups.MapMode)
            {
                Add(YouTubePlaylistNames.Title(video.Map, video.Mode, video.Rank));
            }
        }

        if (groups.Patch)
        {
            string line = GameVersionOrder.PatchLine(currentPatchLine) ?? currentPatchLine;
            Add(YouTubePlaylistNames.Fit(PatchPlaylist.Name(video.GameVersion, line, seasonName)));
        }

        return titles;

        void Add(string title)
        {
            if (!string.IsNullOrWhiteSpace(title) && !titles.Contains(title))
            {
                titles.Add(title);
            }
        }
    }
}
