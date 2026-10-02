using System;
using System.Linq;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube.Search;

namespace HeroesReplay.Core.YouTube.Playlists;

/// <summary>
/// Reads what a channel video shows from its title and description. The current template
/// writes <c>Map:</c>, <c>Mode:</c>, <c>Rank:</c>, and <c>Build:</c> lines. Older uploads
/// have only the title (<c>Sky Temple - 65269475 - Platinum</c>) and a <c>Game type:</c>
/// line, so their build comes from Heroes Profile.
/// </summary>
public static class YouTubeVideoFacts
{
    public static YouTubeLibraryVideo Read(
        string videoId,
        string title,
        string description,
        string privacyStatus
    )
    {
        string[] lines = (description ?? string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        bool clip = lines.Any(line => line.StartsWith("clip:", StringComparison.Ordinal));
        int replayId = YouTubeReplayMatch.IdsIn(title, description).FirstOrDefault();
        string map = Catalog(Label(lines, "Map:"));
        string mode = YouTubePlaylistNames.Mode(
            Label(lines, "Mode:") ?? Label(lines, "Game type:")
        );
        string rank = Label(lines, "Rank:");
        string build = Label(lines, "Build:");
        foreach (string part in (title ?? string.Empty).Split(" - ", StringSplitOptions.None))
        {
            map ??= Catalog(part);
            mode ??= YouTubePlaylistNames.Mode(part);
            if (rank == null && YouTubePlaylistNames.League(part) != null)
            {
                rank = part.Trim();
            }
        }

        return new YouTubeLibraryVideo
        {
            VideoId = videoId?.Trim(),
            ReplayId = replayId > 0 ? replayId : null,
            Kind = clip ? YouTubeLibraryRecord.Clip : YouTubeLibraryRecord.Full,
            Map = map,
            Mode = clip ? null : mode,
            Rank = clip ? null : rank,
            GameVersion = build,
            PrivacyStatus = privacyStatus,
        };
    }

    /// <summary>
    /// Fills what the video did not say from the Heroes Profile replay. A localized map name
    /// is replaced by the English one.
    /// </summary>
    public static void Fill(YouTubeLibraryVideo video, HeroesProfileReplay replay)
    {
        if (video == null || replay == null)
        {
            return;
        }

        if (!EnglishMapNames.IsCatalog(video.Map))
        {
            video.Map = EnglishMapNames.Prefer(replay.Map, video.Map, null);
        }

        if (!video.IsClip)
        {
            video.Mode ??= YouTubePlaylistNames.Mode(replay.GameType);
            if (string.IsNullOrWhiteSpace(video.Rank) && !string.IsNullOrWhiteSpace(replay.Rank))
            {
                video.Rank = replay.Rank;
            }
        }

        if (string.IsNullOrWhiteSpace(video.GameVersion))
        {
            video.GameVersion = replay.GameVersion;
        }
    }

    public static bool NeedsRank(YouTubeLibraryVideo video) =>
        video != null
        && !video.IsClip
        && string.Equals(video.Mode, "Storm League", StringComparison.Ordinal)
        && string.IsNullOrWhiteSpace(video.Rank);

    private static string Label(string[] lines, string label)
    {
        string line = lines.FirstOrDefault(line =>
            line.StartsWith(label, StringComparison.OrdinalIgnoreCase)
        );
        if (line == null)
        {
            return null;
        }

        string value = line.Substring(label.Length).Trim();
        return value.Length == 0 ? null : value;
    }

    private static string Catalog(string map)
    {
        string canonical = EnglishMapNames.Canonical(map);
        return EnglishMapNames.IsCatalog(canonical) ? canonical : null;
    }
}
