using System;
using System.Collections.Generic;
using HeroesReplay.Core.MediaPolicy;

namespace HeroesReplay.Core.YouTube.Publication;

/// <summary>
/// One video the budget counts: a reserved slot at the time it publishes, or a video that is
/// already public. Older reservation lines have no map, rank, or heroes.
/// </summary>
public sealed class PublicationSample
{
    public DateTimeOffset At { get; init; }
    public bool Requested { get; init; }
    public string Map { get; init; }
    public string Rank { get; init; }
    public string Hero { get; init; }
    public IReadOnlyList<string> Heroes { get; init; }
}

/// <summary>
/// The budget picks the publish time. It does not hold an upload back. A public listing is
/// inserted private with the earliest time that keeps the interval, the day and week caps,
/// the reserved request room, and the map, rank, and hero cooldowns. A prelive host still
/// uploads private listings with no time, and both hosts stop at the videos.insert quota-day cap.
/// </summary>
public static class PublicationSchedule
{
    public const int MaxInsertsPerQuotaDay = 80;

    public static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(2);

    private static readonly TimeSpan Day = TimeSpan.FromHours(24);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

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
    public static readonly TimeSpan PublishAhead = TimeSpan.FromDays(7);

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

    /// <summary>
    /// Whether the replay may publish at <paramref name="now"/> itself. The send path uses
    /// <see cref="Plan"/>, which applies the same rules and looks for a later time instead of
    /// refusing. <paramref name="recent"/> only feeds the map, rank, and hero checks.
    /// </summary>
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
        PublicationDecision gate = Gate(settings, facts, publicListing, insertsThisQuotaDay, now);
        if (gate != null)
        {
            return gate;
        }

        List<PublicationSample> paced = History(publicAtUtc, lastPublicUtc, requestedInDay, now);
        List<PublicationSample> shown = Seen(lastMap, lastMapUtc, lastHero, lastHeroUtc);
        AddSamples(shown, recent);
        AddSamples(shown, paced);
        string conflict = Conflict(
            settings,
            facts.Criteria == ReplayMediaPriority.Requested,
            now,
            paced,
            shown,
            map,
            rank,
            hero,
            heroes
        );
        return conflict == null
            ? PublicationDecision.Granted("ready")
            : PublicationDecision.Refused(conflict);
    }

    /// <summary>
    /// The earliest publish time from <paramref name="now"/> that every pacing rule allows.
    /// Each rule looks both ways, at videos already public and at slots already scheduled, so a
    /// later replay can take a free time between two earlier ones. A request skips the map,
    /// rank, and hero checks and may use the reserved request room, so it gets the earliest
    /// time. No time inside <see cref="ReplayMediaPolicySettings.MaxPublishAhead"/> is
    /// <c>horizon</c>, and the recording waits. A granted reason is <c>ready</c> when the time is
    /// now, otherwise the rule that pushed it later. A private listing has no publish time.
    /// <paramref name="seen"/> only feeds the map, rank, and hero checks.
    /// </summary>
    public static PublicationDecision Plan(
        ReplayMediaPolicySettings settings,
        PublicationSendFacts facts,
        bool publicListing,
        int insertsThisQuotaDay,
        DateTimeOffset now,
        IReadOnlyList<PublicationSample> slots,
        string map,
        string rank,
        string hero,
        IReadOnlyList<string> heroes,
        IReadOnlyList<PublicationSample> seen = null
    )
    {
        PublicationDecision gate = Gate(settings, facts, publicListing, insertsThisQuotaDay, now);
        if (gate != null)
        {
            return gate;
        }

        bool requested = facts.Criteria == ReplayMediaPriority.Requested;
        DateTimeOffset horizon = now + settings.MaxPublishAhead;
        TimeSpan reach = Reach(settings);
        List<PublicationSample> paced = Within(slots, now - reach, horizon + reach);
        List<PublicationSample> shown = Within(seen, now - reach, horizon + reach);
        shown.AddRange(paced);
        string waited = null;
        foreach (DateTimeOffset at in Candidates(settings, now, horizon, shown))
        {
            string conflict = Conflict(
                settings,
                requested,
                at,
                paced,
                shown,
                map,
                rank,
                hero,
                heroes
            );
            if (conflict == null)
            {
                return PublicationDecision.Scheduled(waited ?? "ready", at);
            }

            waited ??= conflict;
        }

        return PublicationDecision.Refused("horizon");
    }

    /// <summary>
    /// Media facts, the mode, and the quota decide whether the replay is sent at all. Only a
    /// public listing goes on to the pacing rules. Null means the pacing rules decide.
    /// </summary>
    private static PublicationDecision Gate(
        ReplayMediaPolicySettings settings,
        PublicationSendFacts facts,
        bool publicListing,
        int insertsThisQuotaDay,
        DateTimeOffset now
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

        if (
            send.Criteria == ReplayMediaPriority.Ordinary
            && send.RecordedAtUtc != null
            && now - send.RecordedAtUtc.Value > settings.OrdinaryCandidateMaxAge
        )
        {
            return PublicationDecision.Refused("stale");
        }

        return null;
    }

    /// <summary>The pacing rule a video at <paramref name="at"/> breaks, or null.</summary>
    private static string Conflict(
        ReplayMediaPolicySettings settings,
        bool requested,
        DateTimeOffset at,
        IReadOnlyList<PublicationSample> paced,
        IReadOnlyList<PublicationSample> shown,
        string map,
        string rank,
        string hero,
        IReadOnlyList<string> heroes
    )
    {
        string window = Windows(settings, requested, at, paced);
        if (window != null)
        {
            return window;
        }

        foreach (PublicationSample sample in paced)
        {
            if (Near(at, sample.At, settings.MinimumPublicInterval))
            {
                return "interval";
            }
        }

        if (requested)
        {
            return null;
        }

        if (MapRepeats(map, at, settings.MapCooldown, shown))
        {
            return "map";
        }

        if (RankRepeats(rank, at, settings.RankCooldown, shown))
        {
            return "rank";
        }

        if (
            HeroRepeats(
                hero,
                heroes,
                at,
                settings.FeaturedHeroCooldown,
                settings.MaxSharedHeroes,
                shown
            )
        )
        {
            return "hero";
        }

        return null;
    }

    /// <summary>
    /// A video at <paramref name="at"/> joins every rolling window that ends between
    /// <paramref name="at"/> and one window length later. The fullest of those ends at
    /// <paramref name="at"/> or at a scheduled slot inside that span.
    /// </summary>
    private static string Windows(
        ReplayMediaPolicySettings settings,
        bool requested,
        DateTimeOffset at,
        IReadOnlyList<PublicationSample> paced
    )
    {
        foreach (DateTimeOffset end in Ends(paced, at, Week))
        {
            if (Count(paced, end, Week, out _) >= settings.MaxPublicPerWeek)
            {
                return "week";
            }
        }

        int ordinaryRoom = settings.MaxPublicPerDay - settings.ReservedRequestSlotsPerDay;
        bool reserved = false;
        foreach (DateTimeOffset end in Ends(paced, at, Day))
        {
            int day = Count(paced, end, Day, out int requests);
            if (day >= settings.MaxPublicPerDay)
            {
                return "day";
            }

            if (
                day >= ordinaryRoom
                && (!requested || requests >= settings.ReservedRequestSlotsPerDay)
            )
            {
                reserved = true;
            }
        }

        return reserved ? "reserved" : null;
    }

    private static IEnumerable<DateTimeOffset> Ends(
        IReadOnlyList<PublicationSample> paced,
        DateTimeOffset at,
        TimeSpan length
    )
    {
        yield return at;
        foreach (PublicationSample sample in paced)
        {
            if (sample.At > at && sample.At - at < length)
            {
                yield return sample.At;
            }
        }
    }

    private static int Count(
        IReadOnlyList<PublicationSample> paced,
        DateTimeOffset end,
        TimeSpan length,
        out int requests
    )
    {
        int count = 0;
        requests = 0;
        foreach (PublicationSample sample in paced)
        {
            if (sample.At <= end && end - sample.At < length)
            {
                count++;
                if (sample.Requested)
                {
                    requests++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// The earliest allowed time is now or the moment one rule stops applying to one sample,
    /// which is that sample's time plus the rule's span.
    /// </summary>
    private static SortedSet<DateTimeOffset> Candidates(
        ReplayMediaPolicySettings settings,
        DateTimeOffset now,
        DateTimeOffset horizon,
        IReadOnlyList<PublicationSample> shown
    )
    {
        var times = new SortedSet<DateTimeOffset> { now };
        TimeSpan[] spans =
        {
            settings.MinimumPublicInterval,
            Day,
            Week,
            settings.MapCooldown,
            settings.RankCooldown,
            settings.FeaturedHeroCooldown,
        };
        foreach (PublicationSample sample in shown)
        {
            foreach (TimeSpan span in spans)
            {
                DateTimeOffset time = sample.At + span;
                if (time > now && time <= horizon)
                {
                    times.Add(time);
                }
            }
        }

        return times;
    }

    /// <summary>The longest span any rule looks across. A sample further away changes nothing.</summary>
    private static TimeSpan Reach(ReplayMediaPolicySettings settings)
    {
        TimeSpan reach = Week;
        foreach (
            TimeSpan span in new[]
            {
                settings.MinimumPublicInterval,
                settings.MapCooldown,
                settings.RankCooldown,
                settings.FeaturedHeroCooldown,
            }
        )
        {
            if (span > reach)
            {
                reach = span;
            }
        }

        return reach;
    }

    private static List<PublicationSample> Within(
        IReadOnlyList<PublicationSample> samples,
        DateTimeOffset after,
        DateTimeOffset before
    )
    {
        var kept = new List<PublicationSample>();
        if (samples == null)
        {
            return kept;
        }

        foreach (PublicationSample sample in samples)
        {
            if (sample != null && sample.At > after && sample.At < before)
            {
                kept.Add(sample);
            }
        }

        return kept;
    }

    private static void AddSamples(
        List<PublicationSample> target,
        IReadOnlyList<PublicationSample> samples
    )
    {
        if (samples == null)
        {
            return;
        }

        foreach (PublicationSample sample in samples)
        {
            if (sample != null)
            {
                target.Add(sample);
            }
        }
    }

    /// <summary>
    /// The uploader's older publication ledger as samples. That many of the videos in the
    /// last 24 hours count as requests.
    /// </summary>
    internal static List<PublicationSample> History(
        IReadOnlyList<DateTimeOffset> publicAtUtc,
        DateTimeOffset? lastPublicUtc,
        int requestedInDay,
        DateTimeOffset now
    )
    {
        var samples = new List<PublicationSample>();
        int requests = requestedInDay;
        bool lastListed = lastPublicUtc == null;
        if (publicAtUtc != null)
        {
            for (int i = publicAtUtc.Count - 1; i >= 0; i--)
            {
                DateTimeOffset at = publicAtUtc[i];
                bool request = requests > 0 && at <= now && now - at < Day;
                if (request)
                {
                    requests--;
                }

                lastListed |= at == lastPublicUtc;
                samples.Add(new PublicationSample { At = at, Requested = request });
            }
        }

        if (!lastListed)
        {
            samples.Add(new PublicationSample { At = lastPublicUtc.Value });
        }

        return samples;
    }

    /// <summary>The older ledger's last map and focus hero. They only feed the cooldowns.</summary>
    internal static List<PublicationSample> Seen(
        string lastMap,
        DateTimeOffset? lastMapUtc,
        string lastHero,
        DateTimeOffset? lastHeroUtc
    )
    {
        var seen = new List<PublicationSample>();
        if (!string.IsNullOrWhiteSpace(lastMap) && lastMapUtc != null)
        {
            seen.Add(new PublicationSample { At = lastMapUtc.Value, Map = lastMap });
        }

        if (!string.IsNullOrWhiteSpace(lastHero) && lastHeroUtc != null)
        {
            seen.Add(new PublicationSample { At = lastHeroUtc.Value, Hero = lastHero });
        }

        return seen;
    }

    private static bool Near(DateTimeOffset at, DateTimeOffset other, TimeSpan span)
    {
        return (at - other).Duration() < span;
    }

    private static bool Same(string value, string previous)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !string.IsNullOrWhiteSpace(previous)
            && string.Equals(value.Trim(), previous.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MapRepeats(
        string map,
        DateTimeOffset at,
        TimeSpan cooldown,
        IReadOnlyList<PublicationSample> shown
    )
    {
        foreach (PublicationSample sample in shown)
        {
            if (Same(map, sample.Map) && Near(at, sample.At, cooldown))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RankRepeats(
        string rank,
        DateTimeOffset at,
        TimeSpan cooldown,
        IReadOnlyList<PublicationSample> shown
    )
    {
        string tier = RankKey(rank);
        if (tier == null)
        {
            return false;
        }

        foreach (PublicationSample sample in shown)
        {
            if (Near(at, sample.At, cooldown) && Same(tier, RankKey(sample.Rank)))
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
        DateTimeOffset at,
        TimeSpan cooldown,
        int sharedLimit,
        IReadOnlyList<PublicationSample> shown
    )
    {
        foreach (PublicationSample sample in shown)
        {
            if (Same(hero, sample.Hero) && Near(at, sample.At, cooldown))
            {
                return true;
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

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PublicationSample sample in shown)
        {
            if (!Near(at, sample.At, cooldown))
            {
                continue;
            }

            AddHero(seen, sample.Hero);
            if (sample.Heroes == null)
            {
                continue;
            }

            foreach (string name in sample.Heroes)
            {
                AddHero(seen, name);
            }
        }

        int shared = 0;
        foreach (string name in candidate)
        {
            if (seen.Contains(name))
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

    public static int PublishedIn(
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
