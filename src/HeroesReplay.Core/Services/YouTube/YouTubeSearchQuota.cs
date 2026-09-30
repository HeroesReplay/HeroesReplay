using System;

namespace HeroesReplay.Core.Services.YouTube;

/// <summary>
/// YouTube search.list has its own daily quota. One exhausted response pauses
/// further searches until the next Pacific quota day. Recording does not stop.
/// </summary>
public static class YouTubeSearchQuota
{
    public static bool IsExhausted(Exception exception)
    {
        for (Exception current = exception; current != null; current = current.InnerException)
        {
            string text = current.Message;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (
                text.Contains("Quota exceeded", StringComparison.OrdinalIgnoreCase)
                || text.Contains("rateLimitExceeded", StringComparison.OrdinalIgnoreCase)
                || text.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase)
                || text.Contains("TooManyRequests", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }

    public static DateTimeOffset ResumeAt(DateTimeOffset utcNow)
    {
        DateTimeOffset start = PublicationSchedule.QuotaDayStart(utcNow);
        return start > utcNow ? start : start.AddDays(1);
    }
}
