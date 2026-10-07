using System;
using System.Collections.Generic;
using System.IO;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.YouTube.Publication;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Publication;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PublicationPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Plan_SchedulesAnIntervalRefusalForLaterInsteadOfHoldingTheFile()
    {
        var slots = new[] { Slot(-1) };

        PublicationDecision held = PublicationSchedule.Decide(
            PublicationSchedule.CanarySettings(),
            Ordinary(),
            true,
            0,
            Now,
            Now.AddHours(-1),
            new[] { Now.AddHours(-1) },
            0,
            null,
            null,
            null,
            null,
            null,
            null
        );
        PublicationDecision planned = Plan(Ordinary(), slots);

        Assert.False(held.Allow);
        Assert.Equal("interval", held.Reason);
        Assert.True(planned.Allow);
        Assert.Equal("interval", planned.Reason);
        Assert.Equal(Now.AddHours(1), planned.PublishAtUtc);
    }

    [Fact]
    public void Plan_TakesAFreeTimeBetweenScheduledSlotsAndKeepsTheIntervalBothWays()
    {
        PublicationDecision gap = Plan(Ordinary(), new[] { Slot(-1), Slot(5) });
        PublicationDecision tight = Plan(Ordinary(), new[] { Slot(-1), Slot(2) });

        Assert.Equal(Now.AddHours(1), gap.PublishAtUtc);
        Assert.Equal(Now.AddHours(4), tight.PublishAtUtc);
        Assert.Equal("interval", tight.Reason);
    }

    [Fact]
    public void Plan_KeepsFourOrdinaryVideosInAnyRollingDayAndRoomForRequests()
    {
        var slots = new[] { Slot(-23), Slot(-20), Slot(-10), Slot(-5) };

        PublicationDecision ordinary = Plan(Ordinary(), slots);
        PublicationDecision request = Plan(Requested(), slots);

        Assert.Equal("reserved", ordinary.Reason);
        Assert.Equal(Now.AddHours(1), ordinary.PublishAtUtc);
        Assert.Equal("ready", request.Reason);
        Assert.Equal(Now, request.PublishAtUtc);
    }

    [Fact]
    public void Plan_PutsARequestAheadOfOrdinaryVideosAlreadyScheduled()
    {
        var slots = new[] { Slot(2), Slot(4), Slot(6), Slot(8) };

        PublicationDecision ordinary = Plan(Ordinary(), slots);
        PublicationDecision request = Plan(Requested(), slots);
        PublicationDecision thirdRequest = Plan(
            Requested(),
            new[]
            {
                Slot(2),
                Slot(4),
                Slot(6),
                Slot(8),
                Slot(10, requested: true),
                Slot(12, requested: true),
            }
        );

        Assert.Equal("reserved", ordinary.Reason);
        Assert.Equal(Now.AddHours(26), ordinary.PublishAtUtc);
        Assert.Equal("ready", request.Reason);
        Assert.Equal(Now, request.PublishAtUtc);
        // A request is never paced, even past the reserved room.
        Assert.Equal("ready", thirdRequest.Reason);
        Assert.Equal(Now, thirdRequest.PublishAtUtc);
    }

    [Fact]
    public void Plan_PublishesARequestPastTheWeekCapAndHorizon()
    {
        var slots = new List<PublicationSample>();
        for (int i = 0; i < PublicationSchedule.MaxPublicPerWeek; i++)
        {
            slots.Add(new PublicationSample { At = Now.AddDays(-6).AddMinutes(i * 2) });
        }

        ReplayMediaPolicySettings shortHorizon = PublicationSchedule.CanarySettings();
        shortHorizon.MaxPublishAhead = TimeSpan.FromHours(12);

        PublicationDecision request = Plan(Requested(), slots);
        PublicationDecision beyond = PublicationSchedule.Plan(
            shortHorizon,
            Requested(),
            true,
            0,
            Now,
            slots,
            null,
            null,
            null,
            null
        );

        Assert.Equal("ready", request.Reason);
        Assert.Equal(Now, request.PublishAtUtc);
        Assert.True(beyond.Allow);
        Assert.Equal(Now, beyond.PublishAtUtc);
        Assert.False(Plan(Ordinary(), slots, shortHorizon).Allow);
    }

    [Fact]
    public void Plan_KeepsTheMapRankAndHeroCooldownsOnBothSides()
    {
        var slots = new[]
        {
            new PublicationSample
            {
                At = Now.AddHours(3),
                Map = "Tomb of the Spider Queen",
                Rank = "Diamond 3",
                Hero = "Li-Ming",
            },
        };

        PublicationDecision map = Plan(Ordinary(), slots, map: "tomb of the spider queen");
        PublicationDecision rank = Plan(Ordinary(), slots, map: "Sky Temple", rank: "Diamond 1");
        PublicationDecision hero = Plan(Ordinary(), slots, map: "Sky Temple", hero: "li-ming");
        PublicationDecision other = Plan(
            Ordinary(),
            slots,
            map: "Sky Temple",
            rank: "Gold 2",
            hero: "Illidan"
        );
        PublicationDecision request = Plan(
            Requested(),
            slots,
            map: "Tomb of the Spider Queen",
            rank: "Diamond 2",
            hero: "Li-Ming"
        );

        Assert.Equal("map", map.Reason);
        Assert.Equal(Now.AddHours(11), map.PublishAtUtc);
        Assert.Equal("rank", rank.Reason);
        Assert.Equal(Now.AddHours(11), rank.PublishAtUtc);
        Assert.Equal("hero", hero.Reason);
        Assert.Equal(Now.AddHours(11), hero.PublishAtUtc);
        Assert.Equal("ready", other.Reason);
        Assert.Equal(Now, other.PublishAtUtc);
        Assert.Equal("ready", request.Reason);
        Assert.Equal(Now, request.PublishAtUtc);
    }

    [Fact]
    public void Plan_HoldsTheFileOnlyForTheQuotaAndTheMediaRules()
    {
        PublicationDecision quota = PublicationSchedule.Plan(
            PublicationSchedule.CanarySettings(),
            Requested(),
            true,
            PublicationSchedule.MaxInsertsPerQuotaDay,
            Now,
            Array.Empty<PublicationSample>(),
            null,
            null,
            null,
            null
        );
        PublicationDecision stale = Plan(
            new PublicationSendFacts
            {
                Criteria = ReplayMediaPriority.Ordinary,
                RecordedAtUtc = Now.Add(-PublicationSchedule.OrdinaryMaxAge).AddTicks(-1),
            },
            new[] { Slot(-1) }
        );
        PublicationDecision incomplete = Plan(PublicationSendFacts.Unverified(Now), null);
        PublicationDecision prelive = PublicationSchedule.Plan(
            PublicationSchedule.CanarySettings(),
            Ordinary(),
            false,
            0,
            Now,
            new[] { Slot(-1) },
            null,
            null,
            null,
            null
        );

        Assert.False(quota.Allow);
        Assert.Equal(PublicationSchedule.InsertCap, quota.Reason);
        Assert.Null(quota.PublishAtUtc);
        Assert.False(stale.Allow);
        Assert.Equal("stale", stale.Reason);
        Assert.False(incomplete.Allow);
        Assert.Equal("incomplete", incomplete.Reason);
        Assert.True(prelive.Allow);
        Assert.Equal("private-listing", prelive.Reason);
        Assert.Null(prelive.PublishAtUtc);
    }

    /// <summary>
    /// Eight ordinary games end half an hour apart. Four publish that day, two hours apart,
    /// and four the next day once the first ones leave the rolling day. A paid request that
    /// arrives after all eight are scheduled still publishes the same evening. The next
    /// ordinary game waits for the third day.
    /// </summary>
    [Fact]
    public void TryReserve_SpreadsEightRecordingsOverTwoDaysAndARequestPublishesAtOnce()
    {
        string root = Path.Combine(Path.GetTempPath(), "hr-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "publication-reservations.txt");
        string[] maps =
        {
            "Alterac Pass",
            "Braxis Holdout",
            "Cursed Hollow",
            "Dragon Shire",
            "Garden of Terror",
            "Haunted Mines",
            "Infernal Shrines",
            "Sky Temple",
        };
        string[] ranks = { "Diamond", "Platinum", "Master", "Gold" };
        try
        {
            var times = new List<DateTimeOffset?>();
            for (int i = 0; i < maps.Length; i++)
            {
                DateTimeOffset finished = Now.AddMinutes(30 * i);
                PublicationReservationResult slot = Reserve(
                    path,
                    finished,
                    "replay-" + (100 + i),
                    maps[i],
                    ranks[i % ranks.Length],
                    requested: false
                );
                Assert.True(slot.Allow);
                times.Add(slot.PublishAtUtc);
            }

            PublicationReservationResult request = Reserve(
                path,
                Now.AddHours(4),
                "replay-200",
                "Towers of Doom",
                "Diamond",
                requested: true
            );
            PublicationReservationResult ordinary = Reserve(
                path,
                Now.AddHours(4),
                "replay-201",
                "Battlefield of Eternity",
                "Bronze",
                requested: false
            );

            Assert.Equal(
                new DateTimeOffset?[]
                {
                    Now,
                    Now.AddHours(2),
                    Now.AddHours(4),
                    Now.AddHours(6),
                    Now.AddHours(24),
                    Now.AddHours(26),
                    Now.AddHours(28),
                    Now.AddHours(30),
                },
                times
            );
            Assert.Equal(Now.AddHours(4), request.PublishAtUtc);
            Assert.Equal(Now.AddHours(48), ordinary.PublishAtUtc);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static PublicationReservationResult Reserve(
        string path,
        DateTimeOffset now,
        string workKey,
        string map,
        string rank,
        bool requested
    )
    {
        PublicationSendFacts facts = requested ? Requested() : Ordinary();
        return PublicationReservation.TryReserve(
            path,
            true,
            0,
            now,
            null,
            new List<DateTimeOffset>(),
            0,
            requested,
            now,
            map,
            null,
            null,
            null,
            null,
            null,
            workKey,
            PublicationSchedule.CanarySettings(),
            facts,
            rank
        );
    }

    private static PublicationDecision Plan(
        PublicationSendFacts facts,
        IReadOnlyList<PublicationSample> slots,
        ReplayMediaPolicySettings settings = null,
        string map = null,
        string rank = null,
        string hero = null
    )
    {
        return PublicationSchedule.Plan(
            settings ?? PublicationSchedule.CanarySettings(),
            facts,
            true,
            0,
            Now,
            slots,
            map,
            rank,
            hero,
            null
        );
    }

    private static PublicationSample Slot(int hours, bool requested = false)
    {
        return new PublicationSample { At = Now.AddHours(hours), Requested = requested };
    }

    private static PublicationSendFacts Ordinary()
    {
        return new PublicationSendFacts
        {
            Criteria = ReplayMediaPriority.Ordinary,
            RecordedAtUtc = Now,
        };
    }

    private static PublicationSendFacts Requested()
    {
        return new PublicationSendFacts
        {
            Criteria = ReplayMediaPriority.Requested,
            RecordedAtUtc = Now,
        };
    }
}
