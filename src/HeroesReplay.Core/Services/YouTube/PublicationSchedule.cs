using System;
using System.Collections.Generic;
using HeroesReplay.Core.Services.Media;

namespace HeroesReplay.Core.Services.YouTube;

/// <summary>
/// Public hosts wait for the rolling interval. A prelive host still uploads private
/// listings, and both hosts stop at the videos.insert quota-day cap.
/// </summary>
public static class PublicationSchedule
{
    public const int MaxInsertsPerQuotaDay = 80;

    public static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(2);

    public static bool ConfigurationAllowsSend(ReplayMediaPolicySettings settings)
    {
        return settings != null
            && (settings.LoadErrors == null || settings.LoadErrors.Count == 0)
            && ReplayMediaPolicy.Validate(settings).Count == 0;
    }

    public static bool MayUpload(
        bool productionHost,
        int insertsToday,
        DateTimeOffset now,
        DateTimeOffset? lastInsertUtc
    )
    {
        return MayUpload(productionHost, insertsToday, now, lastInsertUtc, null);
    }

    public static bool MayUpload(
        bool productionHost,
        int insertsToday,
        DateTimeOffset now,
        DateTimeOffset? lastInsertUtc,
        ReplayMediaPolicySettings settings
    )
    {
        if (settings != null && !ConfigurationAllowsSend(settings))
        {
            return false;
        }

        int quota = settings == null ? MaxInsertsPerQuotaDay : settings.MaxInsertsPerQuotaDay;
        TimeSpan interval = settings == null ? MinimumInterval : settings.MinimumPublicInterval;
        if (insertsToday >= quota)
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

        return now - lastInsertUtc.Value >= interval;
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
        return Decide(
            CanarySettings(),
            new PublicationSendFacts
            {
                Criteria = requested ? ReplayMediaPriority.Requested : ReplayMediaPriority.Ordinary,
                RecordedAtUtc = recordedAtUtc,
            },
            productionHost,
            insertsThisQuotaDay,
            now,
            lastPublicUtc,
            publicAtUtc,
            requestedInDay,
            map,
            lastMap,
            lastMapUtc,
            hero,
            lastHero,
            lastHeroUtc
        );
    }

    public static PublicationDecision Decide(
        ReplayMediaPolicySettings settings,
        PublicationSendFacts facts,
        bool productionHost,
        int insertsThisQuotaDay,
        DateTimeOffset now,
        DateTimeOffset? lastPublicUtc,
        IReadOnlyList<DateTimeOffset> publicAtUtc,
        int requestedInDay,
        string map,
        string lastMap,
        DateTimeOffset? lastMapUtc,
        string hero,
        string lastHero,
        DateTimeOffset? lastHeroUtc
    )
    {
        if (!ConfigurationAllowsSend(settings))
        {
            return PublicationDecision.Refused("configuration");
        }

        PublicationSendFacts send = facts ?? PublicationSendFacts.Unverified(null);
        if (send.AlreadyPublished)
        {
            return PublicationDecision.Refused("already-published");
        }

        if (send.Incomplete)
        {
            return PublicationDecision.Refused("incomplete");
        }

        if (send.Uncorrelated)
        {
            return PublicationDecision.Refused("uncorrelated");
        }

        bool requested = send.Criteria == ReplayMediaPriority.Requested;
        PublicationDecision criteria = CriteriaGate(settings, send.Criteria);
        if (criteria != null)
        {
            return criteria;
        }

        if (insertsThisQuotaDay >= settings.MaxInsertsPerQuotaDay)
        {
            return PublicationDecision.Refused("quota");
        }

        if (!productionHost)
        {
            return PublicationDecision.Granted("host");
        }

        int day = CountSince(publicAtUtc, now, TimeSpan.FromHours(24));
        int week = CountSince(publicAtUtc, now, TimeSpan.FromDays(7));
        if (week >= settings.MaxPublicPerWeek)
        {
            return PublicationDecision.Refused("week");
        }

        if (day >= settings.MaxPublicPerDay)
        {
            return PublicationDecision.Refused("day");
        }

        int ordinaryRoom = settings.MaxPublicPerDay - settings.ReservedRequestSlotsPerDay;
        if (!requested && day >= ordinaryRoom)
        {
            return PublicationDecision.Refused("reserved");
        }

        if (
            requested
            && requestedInDay >= settings.ReservedRequestSlotsPerDay
            && day >= ordinaryRoom
        )
        {
            return PublicationDecision.Refused("reserved");
        }

        if (lastPublicUtc != null)
        {
            if (
                now < lastPublicUtc.Value
                || now - lastPublicUtc.Value < settings.MinimumPublicInterval
            )
            {
                return PublicationDecision.Refused("interval");
            }
        }

        if (
            send.Criteria == ReplayMediaPriority.Ordinary
            && send.RecordedAtUtc != null
            && now - send.RecordedAtUtc.Value > settings.OrdinaryCandidateMaxAge
        )
        {
            return PublicationDecision.Refused("stale");
        }

        int penalty = 0;
        if (WithinCooldown(map, lastMap, lastMapUtc, now, settings.MapCooldown))
        {
            penalty++;
        }

        if (WithinCooldown(hero, lastHero, lastHeroUtc, now, settings.FeaturedHeroCooldown))
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

    public static ReplayMediaPolicySettings CanarySettings()
    {
        return new ReplayMediaPolicySettings
        {
            Version = "1",
            RecordingMode = ReplayRecordingMode.All,
            PublicationMode = ReplayPublicationMode.AllEligible,
        };
    }

    private static PublicationDecision CriteriaGate(
        ReplayMediaPolicySettings settings,
        ReplayMediaPriority criteria
    )
    {
        if (settings.PublicationMode == ReplayPublicationMode.Disabled)
        {
            return PublicationDecision.Refused("disabled");
        }

        if (criteria == ReplayMediaPriority.Requested)
        {
            return null;
        }

        if (settings.PublicationMode == ReplayPublicationMode.RequestedOnly)
        {
            return PublicationDecision.Refused("not-requested");
        }

        if (
            settings.PublicationMode == ReplayPublicationMode.Curated
            && criteria != ReplayMediaPriority.Notable
            && criteria != ReplayMediaPriority.HighSkill
        )
        {
            return PublicationDecision.Refused("ordinary");
        }

        return null;
    }

    private static bool WithinCooldown(
        string value,
        string previous,
        DateTimeOffset? previousAt,
        DateTimeOffset now,
        TimeSpan cooldown
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
            && now - previousAt.Value < cooldown;
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

    /// <summary>
    /// A public listing is inserted private and carries the next UTC time YouTube should
    /// publish it. A private listing has no publish time. The next time is now, unless the
    /// previous public video was inside the minimum interval.
    /// </summary>
    public static DateTimeOffset? NextPublishAt(
        string desiredFinal,
        DateTimeOffset nowUtc,
        DateTimeOffset? lastPublicUtc
    )
    {
        return NextPublishAt(desiredFinal, nowUtc, lastPublicUtc, null);
    }

    public static DateTimeOffset? NextPublishAt(
        string desiredFinal,
        DateTimeOffset nowUtc,
        DateTimeOffset? lastPublicUtc,
        TimeSpan? minimumInterval
    )
    {
        if (!string.Equals(desiredFinal, "public", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (nowUtc.Offset != TimeSpan.Zero)
        {
            return null;
        }

        TimeSpan interval = minimumInterval ?? MinimumInterval;
        DateTimeOffset when = nowUtc;
        if (lastPublicUtc != null && lastPublicUtc.Value.Offset == TimeSpan.Zero)
        {
            DateTimeOffset earliest = lastPublicUtc.Value.Add(interval);
            if (earliest > when)
            {
                when = earliest;
            }
        }

        return UploadVisibility.PublishAt(desiredFinal, when);
    }

    public static DateTimeOffset QuotaDayStart(DateTimeOffset utc)
    {
        TimeZoneInfo pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        DateTime local = TimeZoneInfo.ConvertTime(utc, pacific).Date;
        TimeSpan offset = pacific.GetUtcOffset(local);
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset);
    }
}
