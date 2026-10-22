using System;
using System.Collections.Generic;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Playlists;
using HeroesReplay.Core.YouTube.Quota;
using Microsoft.Extensions.Configuration;
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

    /// <summary>
    /// A ranked Storm League video: map, mode and rank, plus a draft note on 43% and a focus hero
    /// on 17% of the channel's videos (#507). Without the mode playlist it is one insert less.
    /// </summary>
    [Fact]
    public void PlaylistUnitsPerVideo_CountsTheGroupsThatAreOn()
    {
        var groups = new YouTubePlaylistSettings();
        Assert.Equal(180, YouTubeQuotaPlan.PlaylistUnitsPerVideo(groups));

        groups.Mode = false;
        Assert.Equal(130, YouTubeQuotaPlan.PlaylistUnitsPerVideo(groups));

        groups.MapMode = true;
        Assert.Equal(180, YouTubeQuotaPlan.PlaylistUnitsPerVideo(groups));

        Assert.Equal(
            0,
            YouTubeQuotaPlan.PlaylistUnitsPerVideo(
                new YouTubePlaylistSettings
                {
                    Map = false,
                    Mode = false,
                    Rank = false,
                    Draft = false,
                    ViewerReview = false,
                    HeroFocus = false,
                }
            )
        );
    }

    /// <summary>
    /// The production overlay (#507): 72 public videos a day at 130 units fit the 9,500 the
    /// library pass may spend, and 95 inserts fit the upload bucket.
    /// </summary>
    [Fact]
    public void Warnings_TheReleasedProductionSettingsFit()
    {
        (YouTubeSettings youtube, ReplayMediaPolicySettings media) = Released();

        Assert.Empty(YouTubeQuotaPlan.Warnings(youtube, media));
        Assert.Equal(95, media.MaxInsertsPerQuotaDay);
        Assert.Equal(9500, youtube.LibraryUnitsPerDay);
    }

    /// <summary>With the mode playlist back on, 72 videos a day at 180 units outgrow the pool.</summary>
    [Fact]
    public void Warnings_TheModePlaylistAtProductionPaceFallsBehind()
    {
        (YouTubeSettings youtube, ReplayMediaPolicySettings media) = Released();
        youtube.Playlists.Mode = true;

        Assert.Contains(
            YouTubeQuotaPlan.Warnings(youtube, media),
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

    /// <summary>The settings a release packages: appsettings.json and the prod overlay.</summary>
    private static (YouTubeSettings YouTube, ReplayMediaPolicySettings Media) Released()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.prod.json")
            .Build();
        return (
            configuration.GetSection("YouTube").Get<YouTubeSettings>(),
            configuration.GetSection("ReplayMedia").Get<ReplayMediaPolicySettings>()
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
