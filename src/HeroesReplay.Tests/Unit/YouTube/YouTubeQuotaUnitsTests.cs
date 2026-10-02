using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Publication;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

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
        QuotaReserveUnits = 1600,
    };

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Uploads_AreCountedButNeverRefused()
    {
        var units = new YouTubeQuotaUnits(directory, settings);

        for (int i = 0; i < 7; i++)
        {
            units.SpendUpload(YouTubeQuotaUnits.VideoInsert, LateInDay);
        }

        YouTubeQuotaDay day = units.Read(LateInDay);
        Assert.Equal(7 * 1600, day.UploadUnits);
        Assert.Equal(0, units.LibraryRoom(day));
        Assert.False(units.TrySpendLibrary(YouTubeQuotaUnits.List, LateInDay));
    }

    [Fact]
    public void MayUpload_HoldsTheNextInsertOnceTheDayHasNoRoomOrYouTubeSaidQuota()
    {
        var units = new YouTubeQuotaUnits(directory, settings);
        int granted = 0;
        while (units.MayUpload(units.Read(LateInDay), LateInDay))
        {
            units.SpendUpload(YouTubeQuotaUnits.VideoInsert, LateInDay);
            granted++;
        }

        var paused = new YouTubeQuotaUnits(
            Path.Combine(directory, "paused"),
            new YouTubeSettings { DailyQuotaUnits = 10000 }
        );
        paused.PauseUploads(NextDay, LateInDay);

        // Six inserts are 9600 units, and a seventh would pass the 10000 a day.
        Assert.Equal(6, granted);
        Assert.True(units.MayUpload(units.Read(NextDay), NextDay));
        Assert.False(paused.MayUpload(paused.Read(LateInDay), LateInDay));
        Assert.True(paused.MayUpload(paused.Read(NextDay), NextDay));
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
    public void Library_NeverPassesTheCeilingLeftByUploads()
    {
        var units = new YouTubeQuotaUnits(directory, settings);
        for (int i = 0; i < 4; i++)
        {
            units.SpendUpload(YouTubeQuotaUnits.VideoInsert, LateInDay);
        }

        // 10000 - 1600 reserve - 6400 uploads leaves 2000.
        Assert.Equal(2000, units.LibraryRoom(units.Read(LateInDay)));
        Assert.False(units.TrySpendLibrary(2001, LateInDay));
        Assert.True(units.TrySpendLibrary(2000, LateInDay));
        Assert.Equal(8400, units.Read(LateInDay).Total);
    }

    [Fact]
    public void Day_RollsOverAtPacificMidnightAndSurvivesAReload()
    {
        var units = new YouTubeQuotaUnits(directory, settings);
        units.SpendUpload(YouTubeQuotaUnits.VideoInsert, LateInDay);
        Assert.True(units.TrySpendLibrary(3000, LateInDay));

        YouTubeQuotaDay reloaded = new YouTubeQuotaUnits(directory, settings).Read(LateInDay);
        Assert.Equal(1600, reloaded.UploadUnits);
        Assert.Equal(3000, reloaded.LibraryUnits);
        Assert.False(new YouTubeQuotaUnits(directory, settings).TrySpendLibrary(1, LateInDay));

        YouTubeQuotaDay next = new YouTubeQuotaUnits(directory, settings).Read(NextDay);
        Assert.Equal(0, next.Total);
        Assert.Equal(PublicationSchedule.QuotaDayStart(NextDay), next.QuotaDay);
        Assert.True(new YouTubeQuotaUnits(directory, settings).TrySpendLibrary(1, NextDay));
    }

    [Fact]
    public void Pause_HoldsTheLibraryUntilTheGivenTime()
    {
        var units = new YouTubeQuotaUnits(directory, settings);
        units.PauseLibrary(NextDay, LateInDay);

        Assert.True(YouTubeQuotaUnits.Paused(units.Read(LateInDay), LateInDay));
        Assert.False(units.TrySpendLibrary(1, LateInDay));
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
            Task.Run(() => uploader.SpendUpload(YouTubeQuotaUnits.VideoInsert, LateInDay))
        );

        Assert.Equal(300, granted);
        YouTubeQuotaDay day = cli.Read(LateInDay);
        Assert.Equal(300, day.LibraryUnits);
        Assert.Equal(1600, day.UploadUnits);

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
