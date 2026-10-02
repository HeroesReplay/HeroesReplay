using System;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Playlists;
using HeroesReplay.Core.YouTube.Publication;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class UploadHandoffTests
{
    [Fact]
    public void ShouldDrain_RetriesALiveUploaderAndSkipsDryRun()
    {
        DateTimeOffset last = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

        Assert.False(UploadDrain.ShouldDrain(dryRun: true, last, last.Add(UploadDrain.Interval)));
        Assert.False(
            UploadDrain.ShouldDrain(
                dryRun: false,
                last,
                last.Add(UploadDrain.Interval).AddSeconds(-1)
            )
        );
        Assert.True(UploadDrain.ShouldDrain(dryRun: false, last, last.Add(UploadDrain.Interval)));
    }

    [Fact]
    public void Apply_InsertsPrivateAndKeepsThePreliveMarker()
    {
        var entry = new YouTubeEntry { Title = "Volskaya Foundry - 1", PrivacyStatus = "public" };

        UploadStaging.Apply(
            entry,
            new YouTubeSettings { PrivacyStatus = "private", TitlePrefix = "[TEST]" }
        );

        Assert.Equal(UploadStaging.InitialPrivacy, entry.PrivacyStatus);
        Assert.StartsWith("[TEST]", entry.Title, StringComparison.Ordinal);
        Assert.False(UploadStaging.AllowsPublicReceipt(entry.PrivacyStatus));
    }

    [Fact]
    public void Apply_InsertsPrivateWhenTheListingIsPublicToo()
    {
        var entry = new YouTubeEntry { Title = "Volskaya Foundry - 1", PrivacyStatus = "public" };

        UploadStaging.Apply(entry, new YouTubeSettings { PrivacyStatus = "public" });

        Assert.Equal("private", entry.PrivacyStatus);
        Assert.False(UploadStaging.AllowsPublicReceipt("private"));
        Assert.True(UploadStaging.AllowsPublicReceipt("public"));
    }

    [Fact]
    public void Apply_SchedulesPublishAtWhenTheProductionListingShouldBePublic()
    {
        DateTimeOffset now = new(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);
        DateTimeOffset last = now.AddHours(-1);
        var entry = new YouTubeEntry
        {
            Title = "Volskaya Foundry - 1",
            PrivacyStatus = "public",
            DesiredPrivacyStatus = "public",
        };

        UploadStaging.Apply(entry, new YouTubeSettings { PrivacyStatus = "public" }, now, last);

        Assert.Equal("private", entry.PrivacyStatus);
        Assert.Equal(last.Add(PublicationSchedule.MinimumInterval), entry.PublishAtUtc);
        Assert.Equal(
            entry.PublishAtUtc,
            UploadVisibility.PublishAt(entry.DesiredPrivacyStatus, entry.PublishAtUtc)
        );
    }

    [Fact]
    public void Apply_DoesNotSchedulePublishAtForAPrivateListing()
    {
        DateTimeOffset now = new(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);
        var entry = new YouTubeEntry
        {
            Title = "Volskaya Foundry - 1",
            PrivacyStatus = "public",
            DesiredPrivacyStatus = "public",
        };

        UploadStaging.Apply(
            entry,
            new YouTubeSettings { PrivacyStatus = "private", TitlePrefix = "[TEST]" },
            now,
            null
        );

        Assert.Equal("private", entry.PrivacyStatus);
        Assert.StartsWith("[TEST]", entry.Title, StringComparison.Ordinal);
        Assert.Null(entry.PublishAtUtc);
    }

    [Fact]
    public void MayUpload_SpacesProductionAndCapsTheQuotaDay()
    {
        DateTimeOffset now = new(2026, 9, 29, 18, 0, 0, TimeSpan.Zero);

        Assert.True(PublicationSchedule.MayUpload(false, 0, now, now.AddMinutes(-1)));
        Assert.False(PublicationSchedule.MayUpload(true, 0, now, now.AddHours(-1)));
        Assert.True(
            PublicationSchedule.MayUpload(
                true,
                0,
                now,
                now.Add(-PublicationSchedule.MinimumInterval)
            )
        );
        Assert.False(
            PublicationSchedule.MayUpload(
                false,
                PublicationSchedule.MaxInsertsPerQuotaDay,
                now,
                null
            )
        );
    }

    [Fact]
    public void QuotaDayStart_RollsAtPacificMidnight()
    {
        DateTimeOffset before = new(2026, 1, 15, 7, 59, 0, TimeSpan.Zero);
        DateTimeOffset after = new(2026, 1, 15, 8, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateTime(2026, 1, 14), PublicationSchedule.QuotaDayStart(before).Date);
        Assert.Equal(new DateTime(2026, 1, 15), PublicationSchedule.QuotaDayStart(after).Date);
    }

    [Fact]
    public void Summarize_SeparatesPublicationDeferralFromQuota()
    {
        PublicationHealthReport deferred = PublicationHealth.Summarize(4, 2, 1, false, "1");
        PublicationHealthReport quota = PublicationHealth.Summarize(4, 80, 0, true, "1");

        Assert.Equal("publication", deferred.Limit);
        Assert.Equal("quota", quota.Limit);
        Assert.Equal(4, deferred.Pending);
        Assert.Equal("1", quota.PolicyVersion);
    }

    [Fact]
    public void Roll_FilesOnlyPublicVideosAndDoesNotDuplicate()
    {
        var entries = new[]
        {
            new YouTubeEntry
            {
                VideoId = "pub",
                PrivacyStatus = "public",
                GameVersion = "2.57.0.98304",
                ReplayId = 1,
            },
            new YouTubeEntry
            {
                VideoId = "pub",
                PrivacyStatus = "public",
                GameVersion = "2.57.0.98304",
                ReplayId = 1,
            },
            new YouTubeEntry
            {
                VideoId = "test",
                PrivacyStatus = "private",
                GameVersion = "2.57.0.98304",
                ReplayId = 2,
            },
            new YouTubeEntry
            {
                VideoId = "old",
                PrivacyStatus = "public",
                GameVersion = "2.55.17.98025",
                ReplayId = 3,
            },
            new YouTubeEntry
            {
                VideoId = "mystery",
                PrivacyStatus = "public",
                ReplayId = 4,
            },
        };

        var rolled = YouTubeLibraryPlanner.Roll(entries, "2.57");

        Assert.Equal(3, rolled.Count);
        Assert.Equal("Patch 2.57", rolled[0].PlaylistTitle);
        Assert.Equal("Patch 2.55 archive", rolled[1].PlaylistTitle);
        Assert.Equal(PatchPlaylist.Unknown, rolled[2].PlaylistTitle);
        Assert.DoesNotContain(rolled, item => item.VideoId == "test");
    }

    [Fact]
    public void Roll_FilesAPrivateStagingEntryOnceYouTubeReportsItPublic()
    {
        var rolled = YouTubeLibraryPlanner.Roll(
            new[]
            {
                new YouTubeEntry
                {
                    VideoId = "staged",
                    PrivacyStatus = "private",
                    ActualPrivacyStatus = "public",
                    GameVersion = "2.57.0.98304",
                    ReplayId = 9,
                },
            },
            "2.57",
            "Season 2026"
        );

        YouTubeLibraryItem item = Assert.Single(rolled);
        Assert.Equal("Season 2026", item.PlaylistTitle);
        Assert.Equal("staged", item.VideoId);
    }
}
