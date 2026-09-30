using System;
using System.Collections.Generic;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;

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

    public static IReadOnlyList<YouTubeLibraryItem> Roll(
        IEnumerable<YouTubeEntry> entries,
        string currentPatchLine
    ) => Roll(entries, currentPatchLine, seasonName: null);

    public static IReadOnlyList<YouTubeLibraryItem> Roll(
        IEnumerable<YouTubeEntry> entries,
        string currentPatchLine,
        string seasonName
    )
    {
        if (entries == null)
        {
            return Array.Empty<YouTubeLibraryItem>();
        }

        string line = GameVersionOrder.PatchLine(currentPatchLine) ?? currentPatchLine;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<YouTubeLibraryItem>();
        foreach (YouTubeEntry entry in entries)
        {
            if (!PatchPlaylist.MayFile(entry))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.VideoId))
            {
                continue;
            }

            string videoId = entry.VideoId.Trim();
            string playlist = PatchPlaylist.Name(entry.GameVersion, line, seasonName);
            string key = playlist + "\n" + videoId;
            if (!seen.Add(key))
            {
                continue;
            }

            items.Add(new YouTubeLibraryItem(playlist, videoId, entry.ReplayId));
        }

        return items;
    }

    public static IReadOnlyList<YouTubeLibraryItem> Combine(
        IReadOnlyList<YouTubeLibraryItem> first,
        IReadOnlyList<YouTubeLibraryItem> second
    )
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<YouTubeLibraryItem>();
        Add(first);
        Add(second);
        return items;

        void Add(IReadOnlyList<YouTubeLibraryItem> source)
        {
            if (source == null)
            {
                return;
            }

            foreach (YouTubeLibraryItem item in source)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.VideoId))
                {
                    continue;
                }

                string key = (item.PlaylistTitle ?? string.Empty) + "\n" + item.VideoId.Trim();
                if (seen.Add(key))
                {
                    items.Add(item);
                }
            }
        }
    }
}
