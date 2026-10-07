using System;
using HeroesReplay.Core.YouTube.Publication;

namespace HeroesReplay.Core.YouTube.Quota;

/// <summary>What a refused YouTube call means for the rest of the day.</summary>
public enum YouTubeQuotaRefusal
{
    None,

    /// <summary>
    /// A short-term throttle (<c>rateLimitExceeded</c>, HTTP 429, a per-minute limit), such as
    /// playlist writes sent too fast. The day's quota is still there.
    /// </summary>
    RateLimited,

    /// <summary>
    /// <c>quotaExceeded</c>, <c>uploadLimitExceeded</c>, or a per-day limit. Nothing more fits in
    /// that bucket until the quota day turns.
    /// </summary>
    DailyQuota,
}

/// <summary>
/// A daily quota response pauses the uploader's library pass, and uploads, until the next
/// Pacific quota day. A rate limit only waits <see cref="RateLimitWait"/>.
/// </summary>
public static class YouTubeListQuota
{
    /// <summary>How long a throttled caller waits before it calls YouTube again: one library cycle.</summary>
    public static readonly TimeSpan RateLimitWait = TimeSpan.FromHours(1);

    public static YouTubeQuotaRefusal Classify(Exception exception)
    {
        bool rateLimited = false;
        for (Exception current = exception; current != null; current = current.InnerException)
        {
            string text = current.Message;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            bool limitText = text.Contains("Quota exceeded", StringComparison.OrdinalIgnoreCase);
            if (
                text.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase)
                || text.Contains("dailyLimitExceeded", StringComparison.OrdinalIgnoreCase)
                || text.Contains("uploadLimitExceeded", StringComparison.OrdinalIgnoreCase)
                || (limitText && text.Contains("per day", StringComparison.OrdinalIgnoreCase))
            )
            {
                return YouTubeQuotaRefusal.DailyQuota;
            }

            if (
                limitText
                || text.Contains("rateLimitExceeded", StringComparison.OrdinalIgnoreCase)
                || text.Contains("TooManyRequests", StringComparison.OrdinalIgnoreCase)
            )
            {
                rateLimited = true;
            }
        }

        return rateLimited ? YouTubeQuotaRefusal.RateLimited : YouTubeQuotaRefusal.None;
    }

    /// <summary>True only for the day's quota. A rate limit is not exhaustion.</summary>
    public static bool IsExhausted(Exception exception) =>
        Classify(exception) == YouTubeQuotaRefusal.DailyQuota;

    /// <summary>True for the day's quota and for a rate limit.</summary>
    public static bool IsRefused(Exception exception) =>
        Classify(exception) != YouTubeQuotaRefusal.None;

    public static DateTimeOffset ResumeAt(DateTimeOffset utcNow)
    {
        DateTimeOffset start = PublicationSchedule.QuotaDayStart(utcNow);
        return start > utcNow ? start : start.AddDays(1);
    }
}
