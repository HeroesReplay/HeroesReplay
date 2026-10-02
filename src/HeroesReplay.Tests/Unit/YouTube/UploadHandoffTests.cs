using System;
using HeroesReplay.Core.YouTube;
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
    public void Schedule_StoresTheReservedTimeWhenTheProductionListingShouldBePublic()
    {
        DateTimeOffset slot = new(2026, 10, 1, 4, 0, 0, TimeSpan.Zero);
        var settings = new YouTubeSettings { PrivacyStatus = "public" };
        var entry = new YouTubeEntry
        {
            Title = "Volskaya Foundry - 1",
            PrivacyStatus = "public",
            DesiredPrivacyStatus = "public",
        };

        UploadStaging.Apply(entry, settings);
        UploadStaging.Schedule(entry, settings, slot);

        Assert.Equal("private", entry.PrivacyStatus);
        Assert.Equal(slot, entry.PublishAtUtc);
        Assert.Equal(
            entry.PublishAtUtc,
            UploadVisibility.PublishAt(entry.DesiredPrivacyStatus, entry.PublishAtUtc)
        );
    }

    [Fact]
    public void Schedule_GivesAPrivateListingNoPublishTime()
    {
        DateTimeOffset slot = new(2026, 10, 1, 4, 0, 0, TimeSpan.Zero);
        var settings = new YouTubeSettings { PrivacyStatus = "private", TitlePrefix = "[TEST]" };
        var entry = new YouTubeEntry
        {
            Title = "Volskaya Foundry - 1",
            PrivacyStatus = "public",
            DesiredPrivacyStatus = "public",
            PublishAtUtc = slot,
        };

        UploadStaging.Apply(entry, settings);
        UploadStaging.Schedule(entry, settings, slot);

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
}
