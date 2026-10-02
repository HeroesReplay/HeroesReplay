using System;
using System.Linq;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube.Search;

namespace HeroesReplay.Core.YouTube.Playlists;

/// <summary>
/// Reads what a channel video shows from its title and description. The current template
/// writes <c>Map:</c>, <c>Mode:</c>, <c>Rank:</c>, and <c>Build:</c> lines, plus
/// <c>Draft:</c> for an unusual draft and <c>Featured:</c> for a request that named a player.
/// Older uploads have only the title (<c>Sky Temple - 65269475 - Platinum</c>) and a
/// <c>Game type:</c> line, so their build comes from Heroes Profile and they have no draft
/// note or named player.
/// </summary>
public static class YouTubeVideoFacts
{
    /// <summary>
    /// Raised when <see cref="Read"/> learns a new fact. The library pass then lists every
    /// channel page once more so videos already in the record pick it up.
    /// </summary>
    public const int Version = 2;

    public static YouTubeLibraryVideo Read(
        string videoId,
        string title,
        string description,
        string privacyStatus
    )
    {
        string[] lines = Lines(description);
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
            Draft = clip ? null : Draft(lines),
            FocusHero = clip ? null : FocusHero(lines),
        };
    }

    /// <summary>
    /// The <c>Draft:</c> note, or null for a usual draft or an older template.
    /// </summary>
    public static string Draft(string[] lines) => Label(lines, "Draft:");

    /// <summary>
    /// The <c>Featured:</c> hero. Only a request that named a player slot writes that line.
    /// </summary>
    public static string FocusHero(string[] lines) => Label(lines, "Featured:");

    /// <summary>
    /// Copies a draft note or named player the record does not have yet from what the channel
    /// shows now. Nothing the record already has is replaced. True when something was copied.
    /// </summary>
    public static bool Learn(YouTubeLibraryVideo known, YouTubeLibraryVideo shown)
    {
        if (known == null || shown == null || known.IsClip)
        {
            return false;
        }

        bool changed = false;
        if (string.IsNullOrWhiteSpace(known.Draft) && !string.IsNullOrWhiteSpace(shown.Draft))
        {
            known.Draft = shown.Draft;
            changed = true;
        }

        if (
            string.IsNullOrWhiteSpace(known.FocusHero)
            && !string.IsNullOrWhiteSpace(shown.FocusHero)
        )
        {
            known.FocusHero = shown.FocusHero;
            changed = true;
        }

        return changed;
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

    private static string[] Lines(string description) =>
        (description ?? string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

    private static string Label(string[] lines, string label)
    {
        if (lines == null)
        {
            return null;
        }

        string line = lines.FirstOrDefault(line =>
            line?.TrimStart().StartsWith(label, StringComparison.OrdinalIgnoreCase) == true
        );
        if (line == null)
        {
            return null;
        }

        string value = line.TrimStart().Substring(label.Length).Trim();
        return value.Length == 0 ? null : value;
    }

    private static string Catalog(string map)
    {
        string canonical = EnglishMapNames.Canonical(map);
        return EnglishMapNames.IsCatalog(canonical) ? canonical : null;
    }
}
