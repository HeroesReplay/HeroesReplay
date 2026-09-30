using System;
using System.Collections.Generic;

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

    public const int MaxPublicPerDay = 6;
    public const int MaxPublicPerWeek = 30;
    public const int ReservedRequestSlotsPerDay = 2;
    public static readonly TimeSpan OrdinaryMaxAge = TimeSpan.FromHours(72);
    public static readonly TimeSpan DiversityCooldown = TimeSpan.FromHours(8);

    public static PublicationDecision Decide(
        bool productionHost,
        int insertsThisQuotaDay,
        DateTimeOffset now,
        DateTimeOffset? lastPublicUtc,
        IReadOnlyList<DateTimeOffset> publicAtUtc,
        int requestedInDay,
        bool requested,
        DateTimeOffset? recordedAtUtc,
        string map,
        string lastMap,
        DateTimeOffset? lastMapUtc,
        string hero,
        string lastHero,
        DateTimeOffset? lastHeroUtc
    )
    {
        if (insertsThisQuotaDay >= MaxInsertsPerQuotaDay)
        {
            return PublicationDecision.Refused("quota");
        }

        if (!productionHost)
        {
            return PublicationDecision.Granted("host");
        }

        int day = CountSince(publicAtUtc, now, TimeSpan.FromHours(24));
        int week = CountSince(publicAtUtc, now, TimeSpan.FromDays(7));
        if (week >= MaxPublicPerWeek)
        {
            return PublicationDecision.Refused("week");
        }

        if (day >= MaxPublicPerDay)
        {
            return PublicationDecision.Refused("day");
        }

        int ordinaryRoom = MaxPublicPerDay - ReservedRequestSlotsPerDay;
        if (!requested && day >= ordinaryRoom)
        {
            return PublicationDecision.Refused("reserved");
        }

        if (requested && requestedInDay >= ReservedRequestSlotsPerDay && day >= ordinaryRoom)
        {
            return PublicationDecision.Refused("reserved");
        }

        if (lastPublicUtc != null)
        {
            if (now < lastPublicUtc.Value || now - lastPublicUtc.Value < MinimumInterval)
            {
                return PublicationDecision.Refused("interval");
            }
        }

        if (!requested && recordedAtUtc != null && now - recordedAtUtc.Value > OrdinaryMaxAge)
        {
            return PublicationDecision.Refused("stale");
        }

        int penalty = 0;
        if (WithinCooldown(map, lastMap, lastMapUtc, now))
        {
            penalty++;
        }

        if (WithinCooldown(hero, lastHero, lastHeroUtc, now))
        {
            penalty++;
        }

        return new PublicationDecision
        {
            Allow = true,
            Reason = penalty == 0 ? "ready" : "cooldown",
            Penalty = penalty,
        };
    }

    private static bool WithinCooldown(
        string value,
        string previous,
        DateTimeOffset? previousAt,
        DateTimeOffset now
    )
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || string.IsNullOrWhiteSpace(previous)
            || previousAt == null
            || now < previousAt.Value
        )
        {
            return false;
        }

        return string.Equals(value.Trim(), previous.Trim(), StringComparison.OrdinalIgnoreCase)
            && now - previousAt.Value < DiversityCooldown;
    }

    public static int PublishedIn(
        IReadOnlyList<DateTimeOffset> times,
        DateTimeOffset now,
        TimeSpan window
    )
    {
        return CountSince(times, now, window);
    }

    private static int CountSince(
        IReadOnlyList<DateTimeOffset> times,
        DateTimeOffset now,
        TimeSpan window
    )
    {
        if (times == null || times.Count == 0)
        {
            return 0;
        }

        int count = 0;
        foreach (DateTimeOffset time in times)
        {
            if (time <= now && now - time < window)
            {
                count++;
            }
        }

        return count;
    }

    public static DateTimeOffset QuotaDayStart(DateTimeOffset utc)
    {
        TimeZoneInfo pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        DateTime local = TimeZoneInfo.ConvertTime(utc, pacific).Date;
        TimeSpan offset = pacific.GetUtcOffset(local);
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset);
    }
}
