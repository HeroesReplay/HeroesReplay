using System;

namespace HeroesReplay.Core.Services.YouTube;

/// <summary>
/// Public hosts wait for the rolling interval. A prelive host still uploads private
/// listings, and both hosts stop at the videos.insert quota-day cap.
/// </summary>
public static class PublicationSchedule
{
    public const int MaxInsertsPerQuotaDay = 80;

    public static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(2);

    public static bool MayUpload(
        bool productionHost,
        int insertsToday,
        DateTimeOffset now,
        DateTimeOffset? lastInsertUtc
    )
    {
        if (insertsToday >= MaxInsertsPerQuotaDay)
        {
            return false;
        }

        if (!productionHost || lastInsertUtc == null)
        {
            return true;
        }

        if (now < lastInsertUtc.Value)
        {
            return false;
        }

        return now - lastInsertUtc.Value >= MinimumInterval;
    }

    public static DateTimeOffset QuotaDayStart(DateTimeOffset utc)
    {
        TimeZoneInfo pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        DateTime local = TimeZoneInfo.ConvertTime(utc, pacific).Date;
        TimeSpan offset = pacific.GetUtcOffset(local);
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset);
    }
}
