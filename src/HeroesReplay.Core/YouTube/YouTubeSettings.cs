using System;
using HeroesReplay.Core.YouTube.Metadata;

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
    /// How long a listing of the channel's uploads stays current. The duplicate lookup
    /// lists the uploads playlist again only after this much time.
    /// </summary>
    public TimeSpan UploadsIndexRefresh { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// The uploader's library pass (uploads listing, backfill, playlist filing) runs at
    /// most once per this interval. The last run time survives a restart.
    /// </summary>
    public TimeSpan LibraryInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Quota units the library pass may spend in one Pacific quota day. Uploads are not
    /// limited by this number.
    /// </summary>
    public int LibraryUnitsPerDay { get; set; } = 3000;

    /// <summary>
    /// The project's daily YouTube quota. The library pass stops before the day's spend
    /// would pass this minus <see cref="QuotaReserveUnits"/>.
    /// </summary>
    public int DailyQuotaUnits { get; set; } = 10000;

    /// <summary>
    /// Units the library pass always leaves unspent for uploads.
    /// </summary>
    public int QuotaReserveUnits { get; set; } = 1600;

    /// <summary>
    /// Heroes Profile lookups one library pass may make for videos whose map, mode, rank,
    /// or build is not in the title or description.
    /// </summary>
    public int LibraryLookupsPerPass { get; set; } = 50;

    /// <summary>
    /// Display name of the current patch playlist. Blank uses the patch number.
    /// </summary>
    public string SeasonName { get; set; }

    public YouTubeTitleSettings Titles { get; set; } = new YouTubeTitleSettings();
}
