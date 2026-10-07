using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube.Publication;

namespace HeroesReplay.Core.YouTube.Quota;

/// <summary>
/// One Pacific quota day in <c>Data\youtube-quota-units.json</c>. YouTube counts
/// <c>videos.insert</c> in its own bucket of calls (Video Uploads per day) and every other
/// call (list calls, playlist writes) in the shared pool of units (Queries per day). The two do
/// not share room.
/// </summary>
public sealed class YouTubeQuotaDay
{
    public DateTimeOffset QuotaDay { get; set; }

    /// <summary><c>videos.insert</c> calls sent this quota day, from the upload bucket.</summary>
    public int UploadCalls { get; set; }

    /// <summary>
    /// Pool units an upload used to be charged (1600 per insert) before YouTube moved
    /// <c>videos.insert</c> into its own bucket. A file written by an older build still has it.
    /// Reading the file turns it into <see cref="UploadCalls"/> and sets it to zero.
    /// </summary>
    public int UploadUnits { get; set; }

    public int LibraryUnits { get; set; }
    public DateTimeOffset? LibraryPausedUntil { get; set; }
    public DateTimeOffset? UploadsPausedUntil { get; set; }

    /// <summary>Units spent from the shared pool. Uploads are not in it.</summary>
    public int Total => UploadUnits + LibraryUnits;
}

/// <summary>
/// The quota the uploader process and <c>youtube library</c> spend in the current Pacific quota
/// day, in <c>Data\youtube-quota-units.json</c>. Every change takes a lock file, so two
/// processes cannot both spend the same room. An upload counts one call in the upload bucket
/// (<see cref="YouTubeSettings.DailyUploadCalls"/>) when it is sent, and a new upload starts only
/// while that bucket has a call left above <see cref="YouTubeSettings.UploadCallReserve"/>. The
/// library pass reserves its units in the shared pool (<see cref="YouTubeSettings.DailyQuotaUnits"/>)
/// before each call and stops when the room is gone. An upload never spends pool units.
/// </summary>
public sealed class YouTubeQuotaUnits
{
    public const string FileName = "youtube-quota-units.json";

    /// <summary>What one <c>videos.insert</c> cost in the pool before 2026-06-01. Only read for old files.</summary>
    public const int LegacyVideoInsertUnits = 1600;
    public const int PlaylistItemInsert = 50;
    public const int PlaylistInsert = 50;
    public const int List = 1;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(10);

    private readonly string path;
    private readonly YouTubeSettings settings;

    public YouTubeQuotaUnits(string dataDirectory, YouTubeSettings settings)
    {
        path = string.IsNullOrWhiteSpace(dataDirectory)
            ? null
            : Path.Combine(dataDirectory, FileName);
        this.settings = settings ?? new YouTubeSettings();
    }

    public YouTubeQuotaDay Read(DateTimeOffset utcNow) => Update(utcNow, _ => false);

    /// <summary>Counts one <c>videos.insert</c> in the upload bucket. The pool is not touched.</summary>
    public void SpendUploadCall(DateTimeOffset utcNow) =>
        Update(
            utcNow,
            day =>
            {
                day.UploadCalls++;
                return true;
            }
        );

    /// <summary>
    /// Adds <paramref name="units"/> to the library spend when the day still has room for
    /// them. False leaves the ledger as it was.
    /// </summary>
    public bool TrySpendLibrary(int units, DateTimeOffset utcNow)
    {
        bool spent = false;
        Update(
            utcNow,
            day =>
            {
                if (Paused(day, utcNow) || units > LibraryRoom(day))
                {
                    return false;
                }

                day.LibraryUnits += Math.Max(0, units);
                spent = true;
                return true;
            }
        );
        return spent;
    }

    public void PauseLibrary(DateTimeOffset until, DateTimeOffset utcNow) =>
        Update(
            utcNow,
            day =>
            {
                day.LibraryPausedUntil = until;
                return true;
            }
        );

    public void PauseUploads(DateTimeOffset until, DateTimeOffset utcNow) =>
        Update(
            utcNow,
            day =>
            {
                day.UploadsPausedUntil = until;
                return true;
            }
        );

    public static bool Paused(YouTubeQuotaDay day, DateTimeOffset utcNow) =>
        day?.LibraryPausedUntil is DateTimeOffset until && utcNow < until;

    /// <summary>The upload calls a day may use: the bucket minus the reserve, never below zero.</summary>
    public static int UsableUploadCalls(YouTubeSettings youtube)
    {
        youtube ??= new YouTubeSettings();
        return Math.Max(0, youtube.DailyUploadCalls - Math.Max(0, youtube.UploadCallReserve));
    }

    /// <summary>
    /// True while the upload bucket has a call left under <see cref="UsableUploadCalls"/> and no
    /// quota response from an upload paused uploads until the next quota day. Library spend
    /// does not matter: it is a different bucket.
    /// </summary>
    public bool MayUpload(YouTubeQuotaDay day, DateTimeOffset utcNow)
    {
        if (day?.UploadsPausedUntil is DateTimeOffset until && utcNow < until)
        {
            return false;
        }

        return InsertsLeft(day) > 0;
    }

    /// <summary>
    /// <see cref="MayUpload(YouTubeQuotaDay, DateTimeOffset)"/>, and an ordinary upload also
    /// leaves one insert for each viewer request still waiting (#161). With two calls left and
    /// one request waiting, an ordinary upload takes one and the request gets the last one, so a
    /// request never waits behind older ordinary recordings.
    /// </summary>
    public bool MayUpload(
        YouTubeQuotaDay day,
        DateTimeOffset utcNow,
        bool requested,
        int requestsWaiting
    )
    {
        if (!MayUpload(day, utcNow))
        {
            return false;
        }

        if (requested || requestsWaiting <= 0)
        {
            return true;
        }

        return InsertsLeft(day) > requestsWaiting;
    }

    /// <summary>How many more <c>videos.insert</c> calls fit in the day's upload bucket.</summary>
    public int InsertsLeft(YouTubeQuotaDay day) =>
        Math.Max(0, UsableUploadCalls(settings) - (day?.UploadCalls ?? 0));

    /// <summary>
    /// When a held upload may start again: the pause a quota response set, or the next Pacific
    /// quota day when the day's upload calls are used. Null when an upload may start now.
    /// </summary>
    public DateTimeOffset? UploadsResumeAt(YouTubeQuotaDay day, DateTimeOffset utcNow)
    {
        if (day?.UploadsPausedUntil is DateTimeOffset until && utcNow < until)
        {
            return until;
        }

        return MayUpload(day, utcNow) ? null : NextQuotaDay(utcNow);
    }

    /// <summary>Midnight Pacific after <paramref name="utcNow"/>, on a 23 or 25 hour day too.</summary>
    public static DateTimeOffset NextQuotaDay(DateTimeOffset utcNow)
    {
        DateTimeOffset start = PublicationSchedule.QuotaDayStart(utcNow);
        return PublicationSchedule.QuotaDayStart(start.AddHours(25));
    }

    public int LibraryRoom(YouTubeQuotaDay day)
    {
        int byLibrary = settings.LibraryUnitsPerDay - day.LibraryUnits;
        int byCeiling = settings.DailyQuotaUnits - settings.QuotaReserveUnits - day.Total;
        return Math.Max(0, Math.Min(byLibrary, byCeiling));
    }

    /// <summary>
    /// An older build charged each upload 1600 pool units. Those units are calls in the upload
    /// bucket, and YouTube never took them from the pool. True when the day changed.
    /// </summary>
    public static bool Migrate(YouTubeQuotaDay day)
    {
        if (day == null || day.UploadUnits <= 0)
        {
            return false;
        }

        int calls = (day.UploadUnits + LegacyVideoInsertUnits - 1) / LegacyVideoInsertUnits;
        day.UploadCalls = Math.Max(day.UploadCalls, calls);
        day.UploadUnits = 0;
        return true;
    }

    private YouTubeQuotaDay Update(DateTimeOffset utcNow, Func<YouTubeQuotaDay, bool> change)
    {
        DateTimeOffset quotaDay = PublicationSchedule.QuotaDayStart(utcNow);
        if (path == null)
        {
            var day = new YouTubeQuotaDay { QuotaDay = quotaDay };
            change(day);
            return day;
        }

        using FileStream held = Lock();
        YouTubeQuotaDay current = Load();
        bool migrated = Migrate(current);
        if (current.QuotaDay != quotaDay)
        {
            current.QuotaDay = quotaDay;
            current.UploadCalls = 0;
            current.UploadUnits = 0;
            current.LibraryUnits = 0;
        }

        if (change(current) || migrated)
        {
            DurableFile.Replace(path, JsonSerializer.Serialize(current, Options));
        }

        return current;
    }

    private YouTubeQuotaDay Load()
    {
        string json = DurableFile.ReadOrAside(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new YouTubeQuotaDay();
        }

        try
        {
            return JsonSerializer.Deserialize<YouTubeQuotaDay>(json) ?? new YouTubeQuotaDay();
        }
        catch (JsonException)
        {
            DurableFile.Aside(path);
            return new YouTubeQuotaDay();
        }
    }

    private FileStream Lock()
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + LockWait;
        while (true)
        {
            FileStream held = DurableFile.TryLock(path + ".lock");
            if (held != null)
            {
                return held;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new IOException("YouTube quota ledger " + path + " stayed locked.");
            }

            Thread.Sleep(20);
        }
    }
}
