using System;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Publication;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubeUploaderHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void QuotaBlockedWithABacklog_IsDegraded()
    {
        DateTimeOffset resume = Now.AddHours(7);

        YouTubeUploaderConcern concern = YouTubeUploaderHealth.Evaluate(
            Input(pending: 3, blocked: true, resume: resume)
        );

        Assert.Equal(YouTubeUploaderHealth.QuotaBlockedCode, concern.Code);
        Assert.Contains("3 recording(s)", concern.Cause, StringComparison.Ordinal);
        Assert.Contains("2026-10-07 19:00:00Z", concern.Cause, StringComparison.Ordinal);
    }

    [Fact]
    public void QuotaBlockedWithNothingWaiting_IsHealthy()
    {
        Assert.Null(YouTubeUploaderHealth.Evaluate(Input(pending: 0, blocked: true)));
    }

    [Fact]
    public void NothingPublicForADayWhileUploadsAreDue_IsDegraded()
    {
        YouTubeUploaderConcern concern = YouTubeUploaderHealth.Evaluate(
            Input(
                tally: new PublicationTallyReport
                {
                    Due = 6,
                    StuckPrivate = 4,
                    LastPublicAt = Now.AddHours(-30),
                }
            )
        );

        Assert.Equal(YouTubeUploaderHealth.NotPublishingCode, concern.Code);
        Assert.Contains("6 upload(s)", concern.Cause, StringComparison.Ordinal);
    }

    [Fact]
    public void NeverPublicWhileUploadsAreDue_IsDegraded()
    {
        YouTubeUploaderConcern concern = YouTubeUploaderHealth.Evaluate(
            Input(tally: new PublicationTallyReport { Due = 1 })
        );

        Assert.Equal(YouTubeUploaderHealth.NotPublishingCode, concern.Code);
        Assert.Contains("ever", concern.Cause, StringComparison.Ordinal);
    }

    [Fact]
    public void RecentPublicOrOnlyFutureSlots_IsHealthy()
    {
        Assert.Null(
            YouTubeUploaderHealth.Evaluate(
                Input(
                    tally: new PublicationTallyReport { Due = 2, LastPublicAt = Now.AddHours(-5) }
                )
            )
        );
        Assert.Null(
            YouTubeUploaderHealth.Evaluate(
                Input(tally: new PublicationTallyReport { Scheduled = 8, Due = 0 })
            )
        );
    }

    [Fact]
    public void DryRunOrPrivateListing_IsNeverDegradedForPublishing()
    {
        var due = new PublicationTallyReport { Due = 3 };

        Assert.Null(
            YouTubeUploaderHealth.Evaluate(
                Input(tally: due, live: false, blocked: true, pending: 4)
            )
        );
        Assert.Null(YouTubeUploaderHealth.Evaluate(Input(tally: due, publicListing: false)));
    }

    private static YouTubeUploaderHealthInput Input(
        int pending = 0,
        bool blocked = false,
        DateTimeOffset? resume = null,
        PublicationTallyReport tally = null,
        bool live = true,
        bool publicListing = true
    ) =>
        new()
        {
            Live = live,
            PublicListing = publicListing,
            Pending = pending,
            QuotaBlocked = blocked,
            UploadsResumeAt = resume,
            Tally = tally ?? new PublicationTallyReport(),
            Now = Now,
        };
}
