using System;
using System.Collections.Generic;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Quota;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Quota;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubeQuotaPlanTests
{
    [Fact]
    public void Describe_NamesBothBucketsAndThePublicationRate()
    {
        string line = YouTubeQuotaPlan.Describe(Production(), ProductionMedia());

        Assert.Contains("100 videos.insert calls a day", line, StringComparison.Ordinal);
        Assert.Contains("95 usable", line, StringComparison.Ordinal);
        Assert.Contains("pool 10000 units", line, StringComparison.Ordinal);
        Assert.Contains("Uploads spend nothing from the pool", line, StringComparison.Ordinal);
        Assert.Contains(
            "about 4 non-request videos go public a day",
            line,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Warnings_ProductionSettingsFit()
    {
        Assert.Empty(YouTubeQuotaPlan.Warnings(Production(), ProductionMedia()));
    }

    [Fact]
    public void Warnings_InsertCapAboveTheUploadBucket()
    {
        ReplayMediaPolicySettings media = ProductionMedia();
        media.MaxInsertsPerQuotaDay = 120;

        IReadOnlyList<string> warnings = YouTubeQuotaPlan.Warnings(Production(), media);

        Assert.Contains(
            warnings,
            w => w.Contains("MaxInsertsPerQuotaDay 120", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Warnings_NoPoolLeftMeansNothingIsConfirmedPublic()
    {
        YouTubeSettings youtube = Production();
        youtube.QuotaReserveUnits = youtube.DailyQuotaUnits;

        IReadOnlyList<string> warnings = YouTubeQuotaPlan.Warnings(youtube, ProductionMedia());

        Assert.Contains(
            warnings,
            w => w.Contains("never confirmed public", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Warnings_LibraryBudgetTooSmallToFileTheDaysVideos()
    {
        YouTubeSettings youtube = Production();
        youtube.LibraryUnitsPerDay = 1000;

        IReadOnlyList<string> warnings = YouTubeQuotaPlan.Warnings(youtube, ProductionMedia());

        Assert.Contains(
            warnings,
            w => w.Contains("Playlists fall behind", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Warnings_RequestRoomTakesTheWholeDay()
    {
        ReplayMediaPolicySettings media = ProductionMedia();
        media.ReservedRequestSlotsPerDay = 6;

        IReadOnlyList<string> warnings = YouTubeQuotaPlan.Warnings(Production(), media);

        Assert.Contains(
            warnings,
            w => w.Contains("Only viewer requests", StringComparison.Ordinal)
        );
    }

    private static YouTubeSettings Production() =>
        new()
        {
            DailyUploadCalls = 100,
            UploadCallReserve = 5,
            DailyQuotaUnits = 10000,
            QuotaReserveUnits = 1000,
            LibraryUnitsPerDay = 8000,
        };

    private static ReplayMediaPolicySettings ProductionMedia() =>
        new()
        {
            MaxPublicPerDay = 6,
            MaxPublicPerWeek = 30,
            ReservedRequestSlotsPerDay = 2,
            MaxInsertsPerQuotaDay = 80,
            MaxPublishAhead = TimeSpan.FromDays(3),
        };
}
