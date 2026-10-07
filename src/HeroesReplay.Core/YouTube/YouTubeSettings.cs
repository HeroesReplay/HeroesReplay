using System;
using HeroesReplay.Core.YouTube.Metadata;
using HeroesReplay.Core.YouTube.Playlists;

namespace HeroesReplay.Core.YouTube;

public class YouTubeSettings
{
    public bool Enabled { get; set; }

    /// <summary>
    /// When true, the uploader records the title and file size and does not call YouTube.
    /// Production sets this to false.
    /// </summary>
    public bool DryRun { get; set; } = true;

    public int ReadyStableReads { get; set; } = 5;
    public int ReadyPollMilliseconds { get; set; } = 2000;
    public bool UploadRequestedReplays { get; set; }
    public string ApiKey { get; set; }
    public string ChannelId { get; set; }
    public string EntryFileName { get; set; }
    public string CategoryId { get; set; }
    public string PrivacyStatus { get; set; }

    /// <summary>
    /// Optional title prefix such as [TEST]. An empty prefix leaves the title unmarked.
    /// </summary>
    public string TitlePrefix { get; set; }
    public string EntryFileNameUploaded { get; set; }

    /// <summary>
    /// The uploader's library pass (uploads listing, backfill, playlist filing) runs at
    /// most once per this interval. The last run time survives a restart.
    /// </summary>
    public TimeSpan LibraryInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// The pause between the library pass's playlist writes. YouTube throttles playlist
    /// inserts sent back to back even when the day's quota is far from spent.
    /// </summary>
    public TimeSpan LibraryWriteSpacing { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Pool units the library pass may spend in one Pacific quota day. Uploads have their own
    /// bucket (<see cref="DailyUploadCalls"/>) and are not limited by this number.
    /// </summary>
    public int LibraryUnitsPerDay { get; set; } = 3000;

    /// <summary>
    /// The project's shared YouTube Data API pool (Queries per day): list calls, playlist
    /// writes, and every call other than <c>videos.insert</c> and <c>search.list</c>. The
    /// library pass stops before the day's spend would pass this minus
    /// <see cref="QuotaReserveUnits"/>. Uploads are not charged here (#250).
    /// </summary>
    public int DailyQuotaUnits { get; set; } = 10000;

    /// <summary>
    /// Pool units the library pass always leaves unspent, a margin for other callers such as
    /// the tools scripts.
    /// </summary>
    public int QuotaReserveUnits { get; set; } = 500;

    /// <summary>
    /// The project's <c>videos.insert</c> bucket (Video Uploads per day). Each upload is one
    /// call here and spends nothing from <see cref="DailyQuotaUnits"/>.
    /// </summary>
    public int DailyUploadCalls { get; set; } = 100;

    /// <summary>
    /// Upload calls a day always leaves unused, so a retry, a dev upload on the same project,
    /// or a call the ledger missed does not run into YouTube's hard limit.
    /// </summary>
    public int UploadCallReserve { get; set; } = 5;

    /// <summary>
    /// Heroes Profile lookups one library pass may make for videos whose map, mode, rank,
    /// or build is not in the title or description.
    /// </summary>
    public int LibraryLookupsPerPass { get; set; } = 50;

    /// <summary>
    /// Display name of the current patch playlist. Blank uses the patch number.
    /// </summary>
    public string SeasonName { get; set; }

    /// <summary>
    /// Which playlist groups the library pass files public videos into.
    /// </summary>
    public YouTubePlaylistSettings Playlists { get; set; } = new YouTubePlaylistSettings();

    public YouTubeTitleSettings Titles { get; set; } = new YouTubeTitleSettings();
}
