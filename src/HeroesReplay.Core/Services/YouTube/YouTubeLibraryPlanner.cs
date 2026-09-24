using System;
using System.Collections.Generic;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.YouTube;

public sealed record YouTubeLibraryItem(string PlaylistTitle, string VideoId, int? ReplayId);

public static class YouTubeLibraryPlanner
{
    public static IReadOnlyList<YouTubeLibraryItem> Select(IEnumerable<YouTubeEntry> entries)
    {
        if (entries == null)
        {
            return Array.Empty<YouTubeLibraryItem>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<YouTubeLibraryItem>();
        foreach (YouTubeEntry entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.VideoId))
            {
                continue;
            }

            string videoId = entry.VideoId.Trim();
            if (!seen.Add(videoId))
            {
                continue;
            }

            string playlist = YouTubePlaylistNames.For(entry);
            if (string.IsNullOrWhiteSpace(playlist))
            {
                continue;
            }

            items.Add(new YouTubeLibraryItem(playlist, videoId, entry.ReplayId));
        }

        return items;
    }
}
