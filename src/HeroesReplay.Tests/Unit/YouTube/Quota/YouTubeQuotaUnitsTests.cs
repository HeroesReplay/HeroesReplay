using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Publication;
using HeroesReplay.Core.YouTube.Quota;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Quota;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class YouTubeQuotaUnitsTests : IDisposable
{
    // 2026-10-02 23:59 and 2026-10-03 00:01 Pacific (PDT is UTC-7).
    private static readonly DateTimeOffset LateInDay = new(2026, 10, 3, 6, 59, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextDay = new(2026, 10, 3, 7, 1, 0, TimeSpan.Zero);

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "hr-yt-units-" + Guid.NewGuid().ToString("N")
    );

    private readonly YouTubeSettings settings = new()
    {
        LibraryUnitsPerDay = 3000,
        DailyQuotaUnits = 10000,
        QuotaReserveUnits = 500,
        DailyUploadCalls = 100,
        UploadCallReserve = 5,
    };

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Uploads_CountCallsAndNeverSpendThePool()
    {
        var units = new YouTubeQuotaUnits(directory, settings);

        for (int i = 0; i < 7; i++)
        {
            units.SpendUploadCall(LateInDay);
        }

        YouTubeQuotaDay day = units.Read(LateInDay);
        Assert.Equal(7, day.UploadCalls);
        Assert.Equal(0, day.UploadUnits);
        Assert.Equal(0, day.Total);
        Assert.Equal(3000, units.LibraryRoom(day));
        Assert.True(units.TrySpendLibrary(YouTubeQuotaUnits.List, LateInDay));
    }

    [Fact]
    public void MayUpload_StopsAtTheUploadBucketLessItsReserveOrWhenYouTubeSaidQuota()
    {
        var units = new YouTubeQuotaUnits(directory, settings);
        int granted = 0;
        while (units.MayUpload(units.Read(LateInDay), LateInDay))
        {
            units.SpendUploadCall(LateInDay);
            granted++;
        }

        var paused = new YouTubeQuotaUnits(Path.Combine(directory, "paused"), settings);
        paused.PauseUploads(NextDay, LateInDay);

        // 100 calls a day, 5 held back. Library spend never takes from this bucket.
        Assert.Equal(95, granted);
        Assert.True(units.MayUpload(units.Read(NextDay), NextDay));
        Assert.False(paused.MayUpload(paused.Read(LateInDay), LateInDay));
        Assert.True(paused.MayUpload(paused.Read(NextDay), NextDay));
    }

    [Fact]
    public void Library_SpendDoesNotHoldUploads()
    {
        var units = new YouTubeQuotaUnits(directory, settings);
        while (units.TrySpendLibrary(YouTubeQuotaUnits.PlaylistItemInsert, LateInDay)) { }

        YouTubeQuotaDay day = units.Read(LateInDay);
        Assert.Equal(0, units.LibraryRoom(day));
        Assert.True(units.MayUpload(day, LateInDay));
        Assert.Equal(95, units.InsertsLeft(day));
    }

    [Fact]
    public void UploadsResumeAt_IsTheNextPacificMidnightNotNull()
    {
        // #161: "uploads paused until (null)" when the bucket, not a quota response, held it.
        var tight = new YouTubeSettings { DailyUploadCalls = 6, UploadCallReserve = 0 };
        var units = new YouTubeQuotaUnits(directory, tight);
        Assert.Null(units.UploadsResumeAt(units.Read(LateInDay), LateInDay));

        for (int i = 0; i < 6; i++)
        {
            units.SpendUploadCall(LateInDay);
        }

        YouTubeQuotaDay spent = units.Read(LateInDay);
        Assert.Equal(6, spent.UploadCalls);
        Assert.Null(spent.UploadsPausedUntil);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 3, 7, 0, 0, TimeSpan.Zero),
            units.UploadsResumeAt(spent, LateInDay)
        );

        var paused = new YouTubeQuotaUnits(Path.Combine(directory, "paused"), settings);
        DateTimeOffset until = LateInDay.AddHours(3);
        paused.PauseUploads(until, LateInDay);
        Assert.Equal(until, paused.UploadsResumeAt(paused.Read(LateInDay), LateInDay));
    }

    [Fact]
    public void NextQuotaDay_FollowsPacificDaylightSaving()
    {
        Assert.Equal(
            new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero),
            YouTubeQuotaUnits.NextQuotaDay(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero))
        );
        Assert.Equal(
            new DateTimeOffset(2027, 3, 15, 7, 0, 0, TimeSpan.Zero),
            YouTubeQuotaUnits.NextQuotaDay(new DateTimeOffset(2027, 3, 14, 12, 0, 0, TimeSpan.Zero))
        );
        Assert.Equal(
            new DateTimeOffset(2026, 10, 4, 7, 0, 0, TimeSpan.Zero),
            YouTubeQuotaUnits.NextQuotaDay(NextDay)
        );
    }

    [Fact]
    public void MayUpload_LeavesTheLastInsertsToWaitingRequests()
    {
        var units = new YouTubeQuotaUnits(
            directory,
            new YouTubeSettings { DailyUploadCalls = 8, UploadCallReserve = 2 }
        );
        for (int i = 0; i < 4; i++)
        {
            units.SpendUploadCall(LateInDay);
        }

        YouTubeQuotaDay twoLeft = units.Read(LateInDay);
        Assert.Equal(2, units.InsertsLeft(twoLeft));
        Assert.True(units.MayUpload(twoLeft, LateInDay, requested: false, requestsWaiting: 1));
        Assert.False(units.MayUpload(twoLeft, LateInDay, requested: false, requestsWaiting: 2));

        units.SpendUploadCall(LateInDay);
        YouTubeQuotaDay oneLeft = units.Read(LateInDay);
        Assert.False(units.MayUpload(oneLeft, LateInDay, requested: false, requestsWaiting: 1));
        Assert.True(units.MayUpload(oneLeft, LateInDay, requested: true, requestsWaiting: 1));
        Assert.True(units.MayUpload(oneLeft, LateInDay, requested: false, requestsWaiting: 0));

        units.SpendUploadCall(LateInDay);
        Assert.False(
            units.MayUpload(units.Read(LateInDay), LateInDay, requested: true, requestsWaiting: 0)
        );
    }

    [Fact]
    public void Library_StopsAtItsDailyUnits()
    {
        var units = new YouTubeQuotaUnits(directory, settings);

        int inserts = 0;
        while (units.TrySpendLibrary(YouTubeQuotaUnits.PlaylistItemInsert, LateInDay))
        {
            inserts++;
        }

        Assert.Equal(60, inserts);
        Assert.Equal(3000, units.Read(LateInDay).LibraryUnits);
    }

    [Fact]
    public void Library_NeverPassesThePoolLessItsReserve()
    {
        var wide = new YouTubeSettings
        {
            LibraryUnitsPerDay = 20000,
            DailyQuotaUnits = 10000,
            QuotaReserveUnits = 500,
        };
        var units = new YouTubeQuotaUnits(directory, wide);
        for (int i = 0; i < 50; i++)
        {
            units.SpendUploadCall(LateInDay);
        }

        // Uploads take nothing from the pool: 10000 - 500 reserve is left for housekeeping.
        Assert.Equal(9500, units.LibraryRoom(units.Read(LateInDay)));
        Assert.False(units.TrySpendLibrary(9501, LateInDay));
        Assert.True(units.TrySpendLibrary(9500, LateInDay));
        Assert.Equal(9500, units.Read(LateInDay).Total);
    }

    [Fact]
    public void Day_RollsOverAtPacificMidnightAndSurvivesAReload()
    {
        var units = new YouTubeQuotaUnits(directory, settings);
        units.SpendUploadCall(LateInDay);
        Assert.True(units.TrySpendLibrary(3000, LateInDay));

        YouTubeQuotaDay reloaded = new YouTubeQuotaUnits(directory, settings).Read(LateInDay);
        Assert.Equal(1, reloaded.UploadCalls);
        Assert.Equal(3000, reloaded.LibraryUnits);
        Assert.False(new YouTubeQuotaUnits(directory, settings).TrySpendLibrary(1, LateInDay));

        YouTubeQuotaDay next = new YouTubeQuotaUnits(directory, settings).Read(NextDay);
        Assert.Equal(0, next.Total);
        Assert.Equal(0, next.UploadCalls);
        Assert.Equal(PublicationSchedule.QuotaDayStart(NextDay), next.QuotaDay);
        Assert.True(new YouTubeQuotaUnits(directory, settings).TrySpendLibrary(1, NextDay));
    }

    /// <summary>
    /// #250: the file an older build wrote on production after six uploads. Its 9600 upload
    /// units become six calls in the upload bucket, and the pool is free again.
    /// </summary>
    [Fact]
    public void OldFile_UploadUnitsBecomeUploadCallsAndFreeThePool()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, YouTubeQuotaUnits.FileName);
        string quotaDay = PublicationSchedule.QuotaDayStart(LateInDay).ToString("o");
        File.WriteAllText(
            path,
            "{\n  \"QuotaDay\": \""
                + quotaDay
                + "\",\n  \"UploadUnits\": 9600,\n  \"LibraryUnits\": 120,\n  \"LibraryPausedUntil\": null,\n  \"UploadsPausedUntil\": null\n}"
        );
        var units = new YouTubeQuotaUnits(directory, settings);

        YouTubeQuotaDay day = units.Read(LateInDay);

        Assert.Equal(6, day.UploadCalls);
        Assert.Equal(0, day.UploadUnits);
        Assert.Equal(120, day.LibraryUnits);
        Assert.Equal(120, day.Total);
        Assert.Equal(89, units.InsertsLeft(day));
        Assert.True(units.MayUpload(day, LateInDay));
        Assert.Equal(2880, units.LibraryRoom(day));
        Assert.Contains("\"UploadCalls\": 6", File.ReadAllText(path), StringComparison.Ordinal);

        // A file from a day before is reset as before.
        Assert.Equal(0, new YouTubeQuotaUnits(directory, settings).Read(NextDay).UploadCalls);
    }

    [Fact]
    public void Migrate_RoundsAPartialInsertUpAndKeepsNewerCalls()
    {
        var partial = new YouTubeQuotaDay { UploadUnits = 1601 };
        var newer = new YouTubeQuotaDay { UploadUnits = 1600, UploadCalls = 4 };
        var clean = new YouTubeQuotaDay { UploadCalls = 3 };

        Assert.True(YouTubeQuotaUnits.Migrate(partial));
        Assert.True(YouTubeQuotaUnits.Migrate(newer));
        Assert.False(YouTubeQuotaUnits.Migrate(clean));
        Assert.Equal(2, partial.UploadCalls);
        Assert.Equal(4, newer.UploadCalls);
        Assert.Equal(3, clean.UploadCalls);
    }

    [Fact]
    public void Pause_HoldsTheLibraryUntilTheGivenTime()
    {
        var units = new YouTubeQuotaUnits(directory, settings);
        units.PauseLibrary(NextDay, LateInDay);

        Assert.True(YouTubeQuotaUnits.Paused(units.Read(LateInDay), LateInDay));
        Assert.False(units.TrySpendLibrary(1, LateInDay));
        Assert.True(units.MayUpload(units.Read(LateInDay), LateInDay));
        Assert.False(YouTubeQuotaUnits.Paused(units.Read(NextDay), NextDay));
        Assert.True(units.TrySpendLibrary(1, NextDay));
    }

    [Fact]
    public async Task TwoProcesses_CannotSpendTheSameRoomTwice()
    {
        settings.LibraryUnitsPerDay = 300;
        var uploader = new YouTubeQuotaUnits(directory, settings);
        var cli = new YouTubeQuotaUnits(directory, settings);
        int granted = 0;

        await Task.WhenAll(
            Task.Run(() => Spend(uploader)),
            Task.Run(() => Spend(cli)),
            Task.Run(() => uploader.SpendUploadCall(LateInDay))
        );

        Assert.Equal(300, granted);
        YouTubeQuotaDay day = cli.Read(LateInDay);
        Assert.Equal(300, day.LibraryUnits);
        Assert.Equal(1, day.UploadCalls);

        void Spend(YouTubeQuotaUnits units)
        {
            for (int i = 0; i < 200; i++)
            {
                if (units.TrySpendLibrary(1, LateInDay))
                {
                    Interlocked.Increment(ref granted);
                }
            }
        }
    }
}
