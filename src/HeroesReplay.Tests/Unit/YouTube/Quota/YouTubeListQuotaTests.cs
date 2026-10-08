using System;
using System.Net;
using Google;
using Google.Apis.Requests;
using HeroesReplay.Core.YouTube.Quota;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Quota;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubeListQuotaTests
{
    [Theory]
    [InlineData(
        "The service youtube has thrown an exception. HttpStatusCode is Forbidden. The request cannot be completed because you have exceeded your quota. [quotaExceeded]"
    )]
    [InlineData(
        "HttpStatusCode is TooManyRequests. Quota exceeded for quota metric 'Video Uploads' and limit 'Video Uploads per day' of service 'youtube.googleapis.com'. [rateLimitExceeded]"
    )]
    [InlineData("Daily Limit Exceeded. [dailyLimitExceeded]")]
    [InlineData(
        "HttpStatusCode is Forbidden. The user has exceeded the number of videos they may upload. [uploadLimitExceeded]"
    )]
    public void DailyQuota_PausesUntilTheQuotaDayTurns(string message)
    {
        var refused = new InvalidOperationException(message);

        Assert.Equal(YouTubeQuotaRefusal.DailyQuota, YouTubeListQuota.Classify(refused));
        Assert.True(YouTubeListQuota.IsExhausted(refused));
        Assert.True(YouTubeListQuota.IsRefused(refused));
    }

    [Theory]
    [InlineData(
        "HttpStatusCode is TooManyRequests. Quota exceeded for quota metric 'Queries' and limit 'Queries per minute' of service 'youtube.googleapis.com'. [rateLimitExceeded]"
    )]
    [InlineData("HttpStatusCode is Forbidden. Rate Limit Exceeded [rateLimitExceeded]")]
    [InlineData("Response status code does not indicate success: 429 (TooManyRequests).")]
    public void RateLimit_IsNotTheDailyQuota(string message)
    {
        var refused = new InvalidOperationException(message);

        Assert.Equal(YouTubeQuotaRefusal.RateLimited, YouTubeListQuota.Classify(refused));
        Assert.False(YouTubeListQuota.IsExhausted(refused));
        Assert.True(YouTubeListQuota.IsRefused(refused));
    }

    [Fact]
    public void GoogleRateLimitReason_IsARateLimit()
    {
        // playlists.insert on 2026-10-08: 429, "Resource has been exhausted (e.g. check quota)."
        var status = new GoogleApiException("youtube", "Resource has been exhausted.")
        {
            HttpStatusCode = HttpStatusCode.TooManyRequests,
        };
        var reason = new GoogleApiException("youtube", "Resource has been exhausted.")
        {
            Error = new RequestError
            {
                Errors = [new SingleError { Reason = "RATE_LIMIT_EXCEEDED" }],
            },
        };
        var daily = new GoogleApiException("youtube", "Forbidden.")
        {
            HttpStatusCode = HttpStatusCode.Forbidden,
            Error = new RequestError { Errors = [new SingleError { Reason = "quotaExceeded" }] },
        };

        Assert.Equal(YouTubeQuotaRefusal.RateLimited, YouTubeListQuota.Classify(status));
        Assert.Equal(YouTubeQuotaRefusal.RateLimited, YouTubeListQuota.Classify(reason));
        Assert.Equal(YouTubeQuotaRefusal.DailyQuota, YouTubeListQuota.Classify(daily));
    }

    [Fact]
    public void InnerDailyQuota_WinsOverAnOuterRateLimit()
    {
        var refused = new InvalidOperationException(
            "TooManyRequests",
            new InvalidOperationException("[quotaExceeded]")
        );

        Assert.Equal(YouTubeQuotaRefusal.DailyQuota, YouTubeListQuota.Classify(refused));
    }

    [Fact]
    public void OtherErrors_AreNotQuota()
    {
        Assert.Equal(
            YouTubeQuotaRefusal.None,
            YouTubeListQuota.Classify(new InvalidOperationException("playlistNotFound"))
        );
        Assert.Equal(YouTubeQuotaRefusal.None, YouTubeListQuota.Classify(null));
    }
}
