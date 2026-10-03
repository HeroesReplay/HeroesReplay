using System;
using HeroesReplay.Core.YouTube.Publication;

namespace HeroesReplay.Core.YouTube.Quota;

/// <summary>
/// One exhausted quota response pauses the uploader's library pass until the next Pacific
/// quota day. Uploads and recording do not stop.
/// </summary>
public static class YouTubeListQuota
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
