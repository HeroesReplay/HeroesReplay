using System;
using System.Collections.Generic;
using System.Globalization;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.YouTube.Publication;

namespace HeroesReplay.Core.YouTube.Quota;

/// <summary>
/// The effective quota settings in one line, and the places where the publication policy does
/// not fit the YouTube buckets. The uploader logs both once when it starts (#250).
/// </summary>
public static class YouTubeQuotaPlan
{
    /// <summary>
    /// Playlist inserts a ranked Storm League video gets with the default groups (map, mode,
    /// rank, patch), plus about one draft note. 50 units each.
    /// </summary>
    public const int PlaylistUnitsPerVideo = 5 * YouTubeQuotaUnits.PlaylistItemInsert;

    public static string Describe(YouTubeSettings youtube, ReplayMediaPolicySettings media)
    {
        youtube ??= new YouTubeSettings();
        media ??= new ReplayMediaPolicySettings();
        return string.Format(
            CultureInfo.InvariantCulture,
            "YouTube quota: uploads {0} videos.insert calls a day ({1} held back, {2} usable, MaxInsertsPerQuotaDay {3}); pool {4} units a day for list and playlist calls ({5} held back, library pass {6}). Uploads spend nothing from the pool. Publication: {7} a day, {8} a week, {9} reserved for requests, interval {10}, ahead {11}; about {12:0.#} non-request videos go public a day.",
            youtube.DailyUploadCalls,
            youtube.UploadCallReserve,
            YouTubeQuotaUnits.UsableUploadCalls(youtube),
            media.MaxInsertsPerQuotaDay,
            youtube.DailyQuotaUnits,
            youtube.QuotaReserveUnits,
            youtube.LibraryUnitsPerDay,
            media.MaxPublicPerDay,
            media.MaxPublicPerWeek,
            media.ReservedRequestSlotsPerDay,
            media.MinimumPublicInterval,
            media.MaxPublishAhead,
            PublicationSchedule.OrdinaryPublishPerDay(media)
        );
    }

    /// <summary>Each way the settings cannot all hold. Empty when they fit.</summary>
    public static IReadOnlyList<string> Warnings(
        YouTubeSettings youtube,
        ReplayMediaPolicySettings media
    )
    {
        youtube ??= new YouTubeSettings();
        media ??= new ReplayMediaPolicySettings();
        var warnings = new List<string>();
        int usable = YouTubeQuotaUnits.UsableUploadCalls(youtube);
        if (usable <= 0)
        {
            warnings.Add(
                $"YouTube:DailyUploadCalls {youtube.DailyUploadCalls} minus YouTube:UploadCallReserve {youtube.UploadCallReserve} leaves no upload calls. Nothing is uploaded."
            );
        }
        else if (media.MaxInsertsPerQuotaDay > usable)
        {
            warnings.Add(
                $"ReplayMedia:MaxInsertsPerQuotaDay {media.MaxInsertsPerQuotaDay} is above the {usable} usable upload calls a day. The upload bucket stops uploads first."
            );
        }

        int pool = youtube.DailyQuotaUnits - youtube.QuotaReserveUnits;
        if (pool <= 0)
        {
            warnings.Add(
                $"YouTube:DailyQuotaUnits {youtube.DailyQuotaUnits} minus YouTube:QuotaReserveUnits {youtube.QuotaReserveUnits} leaves no pool units. The library pass never runs, so scheduled videos are never confirmed public or filed."
            );
        }
        else if (youtube.LibraryUnitsPerDay > pool)
        {
            warnings.Add(
                $"YouTube:LibraryUnitsPerDay {youtube.LibraryUnitsPerDay} is above the {pool} pool units left after the reserve. The pool stops the library pass first."
            );
        }

        int library = Math.Min(Math.Max(0, youtube.LibraryUnitsPerDay), Math.Max(0, pool));
        int filing = media.MaxPublicPerDay * PlaylistUnitsPerVideo;
        if (library > 0 && filing > library)
        {
            warnings.Add(
                $"Filing {media.MaxPublicPerDay} public videos a day into playlists takes about {filing} units, above the {library} the library pass may spend. Playlists fall behind."
            );
        }

        if (media.MaxPublicPerDay <= media.ReservedRequestSlotsPerDay)
        {
            warnings.Add(
                $"ReplayMedia:MaxPublicPerDay {media.MaxPublicPerDay} is not above ReservedRequestSlotsPerDay {media.ReservedRequestSlotsPerDay}. Only viewer requests are published."
            );
        }

        if (media.MaxPublicPerWeek < media.MaxPublicPerDay)
        {
            warnings.Add(
                $"ReplayMedia:MaxPublicPerWeek {media.MaxPublicPerWeek} is below MaxPublicPerDay {media.MaxPublicPerDay}. The week cap decides."
            );
        }

        double perDay = PublicationSchedule.OrdinaryPublishPerDay(media);
        if (usable > 0 && perDay > usable)
        {
            warnings.Add(
                $"About {perDay:0.#} videos may go public a day, more than the {usable} upload calls a day can send."
            );
        }

        return warnings;
    }
}
