using System;
using System.Collections.Generic;
using HeroesReplay.Core.MediaPolicy;

namespace HeroesReplay.Core.YouTube.Publication;

/// <summary>One recent upload the diversity check can see. Older reservation lines have none of these fields.</summary>
public sealed class PublicationSample
{
    public DateTimeOffset At { get; init; }
    public string Map { get; init; }
    public string Rank { get; init; }
    public string Hero { get; init; }
    public IReadOnlyList<string> Heroes { get; init; }
}

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
        bool publicListing,
        int insertsToday,
        DateTimeOffset now,
        DateTimeOffset? lastInsertUtc
    )
    {
        return MayUpload(publicListing, insertsToday, now, lastInsertUtc, null);
    }

    public static bool MayUpload(
        bool publicListing,
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

        if (!publicListing || lastInsertUtc == null)
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
    public const int MaxSharedHeroes = 4;
    public static readonly TimeSpan OrdinaryMaxAge = TimeSpan.FromHours(72);
    public static readonly TimeSpan DiversityCooldown = TimeSpan.FromHours(8);

    public static PublicationDecision Decide(
        bool publicListing,
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
        DateTimeOffset? lastHeroUtc,
        string rank = null,
        IReadOnlyList<string> heroes = null,
        IReadOnlyList<PublicationSample> recent = null
    )
    {
        return Decide(
            CanarySettings(),
            new PublicationSendFacts
            {
                Criteria = requested ? ReplayMediaPriority.Requested : ReplayMediaPriority.Ordinary,
                RecordedAtUtc = recordedAtUtc,
            },
            publicListing,
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
            lastHeroUtc,
            rank,
            heroes,
            recent
        );
    }

    public static PublicationDecision Decide(
        ReplayMediaPolicySettings settings,
        PublicationSendFacts facts,
        bool publicListing,
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
        DateTimeOffset? lastHeroUtc,
        string rank = null,
        IReadOnlyList<string> heroes = null,
        IReadOnlyList<PublicationSample> recent = null
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

        if (!publicListing)
        {
            return PublicationDecision.Granted("private-listing");
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

        if (!requested)
        {
            PublicationDecision diversity = Diversity(
                settings,
                now,
                map,
                rank,
                hero,
                heroes,
                lastMap,
                lastMapUtc,
                lastHero,
                lastHeroUtc,
                recent
            );
            if (diversity != null)
            {
                return diversity;
            }
        }

        return PublicationDecision.Granted("ready");
    }

    private static PublicationDecision Diversity(
        ReplayMediaPolicySettings settings,
        DateTimeOffset now,
        string map,
        string rank,
        string hero,
        IReadOnlyList<string> heroes,
        string lastMap,
        DateTimeOffset? lastMapUtc,
        string lastHero,
        DateTimeOffset? lastHeroUtc,
        IReadOnlyList<PublicationSample> recent
    )
    {
        if (MapRepeats(map, lastMap, lastMapUtc, now, settings.MapCooldown, recent))
        {
            return PublicationDecision.Refused("map");
        }

        if (RankRepeats(rank, now, settings.RankCooldown, recent))
        {
            return PublicationDecision.Refused("rank");
        }

        if (
            HeroRepeats(
                hero,
                heroes,
                lastHero,
                lastHeroUtc,
                now,
                settings.FeaturedHeroCooldown,
                settings.MaxSharedHeroes,
                recent
            )
        )
        {
            return PublicationDecision.Refused("hero");
        }

        return null;
    }

    private static bool MapRepeats(
        string map,
        string lastMap,
        DateTimeOffset? lastMapUtc,
        DateTimeOffset now,
        TimeSpan cooldown,
        IReadOnlyList<PublicationSample> recent
    )
    {
        if (WithinCooldown(map, lastMap, lastMapUtc, now, cooldown))
        {
            return true;
        }

        if (recent == null)
        {
            return false;
        }

        foreach (PublicationSample sample in recent)
        {
            if (sample != null && WithinCooldown(map, sample.Map, sample.At, now, cooldown))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RankRepeats(
        string rank,
        DateTimeOffset now,
        TimeSpan cooldown,
        IReadOnlyList<PublicationSample> recent
    )
    {
        string tier = RankKey(rank);
        if (tier == null || recent == null)
        {
            return false;
        }

        foreach (PublicationSample sample in recent)
        {
            if (sample == null || now < sample.At || now - sample.At >= cooldown)
            {
                continue;
            }

            string previous = RankKey(sample.Rank);
            if (
                previous != null
                && string.Equals(tier, previous, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static string RankKey(string rank)
    {
        string display = ReplayMediaRanks.Display(rank);
        if (display != null)
        {
            return display;
        }

        return string.IsNullOrWhiteSpace(rank) ? null : rank.Trim();
    }

    private static bool HeroRepeats(
        string hero,
        IReadOnlyList<string> heroes,
        string lastHero,
        DateTimeOffset? lastHeroUtc,
        DateTimeOffset now,
        TimeSpan cooldown,
        int sharedLimit,
        IReadOnlyList<PublicationSample> recent
    )
    {
        if (WithinCooldown(hero, lastHero, lastHeroUtc, now, cooldown))
        {
            return true;
        }

        if (recent != null && !string.IsNullOrWhiteSpace(hero))
        {
            foreach (PublicationSample sample in recent)
            {
                if (sample != null && WithinCooldown(hero, sample.Hero, sample.At, now, cooldown))
                {
                    return true;
                }
            }
        }

        if (sharedLimit <= 0)
        {
            return false;
        }

        var candidate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddHero(candidate, hero);
        if (heroes != null)
        {
            foreach (string name in heroes)
            {
                AddHero(candidate, name);
            }
        }

        if (candidate.Count == 0)
        {
            return false;
        }

        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (
            !string.IsNullOrWhiteSpace(lastHero)
            && lastHeroUtc != null
            && now >= lastHeroUtc.Value
            && now - lastHeroUtc.Value < cooldown
        )
        {
            AddHero(shown, lastHero);
        }

        if (recent != null)
        {
            foreach (PublicationSample sample in recent)
            {
                if (sample == null || now < sample.At || now - sample.At >= cooldown)
                {
                    continue;
                }

                AddHero(shown, sample.Hero);
                if (sample.Heroes == null)
                {
                    continue;
                }

                foreach (string name in sample.Heroes)
                {
                    AddHero(shown, name);
                }
            }
        }

        int shared = 0;
        foreach (string name in candidate)
        {
            if (shown.Contains(name))
            {
                shared++;
            }
        }

        return shared >= sharedLimit;
    }

    private static void AddHero(HashSet<string> names, string hero)
    {
        if (!string.IsNullOrWhiteSpace(hero))
        {
            names.Add(hero.Trim());
        }
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
