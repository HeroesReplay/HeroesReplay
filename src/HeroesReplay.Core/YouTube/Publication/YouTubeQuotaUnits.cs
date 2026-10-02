using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.YouTube.Publication;

public sealed class YouTubeQuotaDay
{
    public DateTimeOffset QuotaDay { get; set; }
    public int UploadUnits { get; set; }
    public int LibraryUnits { get; set; }
    public DateTimeOffset? LibraryPausedUntil { get; set; }
    public DateTimeOffset? UploadsPausedUntil { get; set; }

    public int Total => UploadUnits + LibraryUnits;
}

/// <summary>
/// Quota units spent in the current Pacific quota day by the uploader process and
/// <c>youtube library</c>, in <c>Data\youtube-quota-units.json</c>. Every change takes a
/// lock file, so two processes cannot both spend the same room. An upload is counted when it
/// is sent, and a new upload starts only while the day has room for one more insert.
/// The library pass reserves its units here before each call and stops when the room is gone.
/// </summary>
public sealed class YouTubeQuotaUnits
{
    public const string FileName = "youtube-quota-units.json";
    public const int VideoInsert = 1600;
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

    public void SpendUpload(int units, DateTimeOffset utcNow) =>
        Update(
            utcNow,
            day =>
            {
                day.UploadUnits += Math.Max(0, units);
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

    /// <summary>
    /// True while the day has room for one more <c>videos.insert</c> under
    /// <see cref="YouTubeSettings.DailyQuotaUnits"/>, library spend included, and no quota
    /// response from an upload paused uploads until the next quota day.
    /// </summary>
    public bool MayUpload(YouTubeQuotaDay day, DateTimeOffset utcNow)
    {
        if (day == null)
        {
            return true;
        }

        if (day.UploadsPausedUntil is DateTimeOffset until && utcNow < until)
        {
            return false;
        }

        return settings.DailyQuotaUnits - day.Total >= VideoInsert;
    }

    /// <summary>
    /// <see cref="MayUpload(YouTubeQuotaDay, DateTimeOffset)"/>, and an ordinary upload also
    /// leaves one insert for each viewer request still waiting (#161). With 6 inserts a day and
    /// one request waiting, ordinary uploads take 5 and the request gets the last one, so a
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

        if (requested || requestsWaiting <= 0 || day == null)
        {
            return true;
        }

        return InsertsLeft(day) > requestsWaiting;
    }

    /// <summary>How many more <c>videos.insert</c> calls fit in the day's units.</summary>
    public int InsertsLeft(YouTubeQuotaDay day)
    {
        int room = settings.DailyQuotaUnits - (day?.Total ?? 0);
        return room <= 0 ? 0 : room / VideoInsert;
    }

    /// <summary>
    /// When a held upload may start again: the pause a quota response set, or the next Pacific
    /// quota day when the day's units are spent. Null when an upload may start now.
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
        if (current.QuotaDay != quotaDay)
        {
            current.QuotaDay = quotaDay;
            current.UploadUnits = 0;
            current.LibraryUnits = 0;
        }

        if (change(current))
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
