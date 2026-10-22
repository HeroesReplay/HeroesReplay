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

    [Fact]
    public void Plan_StreamPcPublishesTwelveHalfHourlyGamesAnHourApart()
    {
        // docs/youtube-uploader.md, How often: twelve ordinary games end every half hour
        // from 10:00, with no map repeating and no rank. Each publishes an hour after the last.
        string[] maps =
        {
            "Infernal Shrines",
            "Sky Temple",
            "Cursed Hollow",
            "Dragon Shire",
            "Towers of Doom",
            "Tomb of the Spider Queen",
            "Volskaya Foundry",
            "Garden of Terror",
            "Blackheart's Bay",
            "Warhead Junction",
            "Alterac Pass",
            "Braxis Holdout",
        };
        var slots = new List<PublicationSample>();

        for (int game = 0; game < maps.Length; game++)
        {
            DateTimeOffset ended = Now.AddMinutes(30 * game);
            PublicationDecision planned = PublicationSchedule.Plan(
                StreamPcSettings(),
                new PublicationSendFacts
                {
                    Criteria = ReplayMediaPriority.Ordinary,
                    RecordedAtUtc = ended,
                },
                true,
                game,
                ended,
                slots,
                maps[game],
                null,
                null,
                null
            );

            Assert.True(planned.Allow);
            Assert.Equal(Now.AddHours(game), planned.PublishAtUtc);
            slots.Add(new PublicationSample { At = planned.PublishAtUtc.Value, Map = maps[game] });
        }
    }

    [Fact]
    public void Plan_StreamPcKeepsTheSameRankTwoHoursApart()
    {
        var slots = new[]
        {
            new PublicationSample { At = Now, Rank = "Gold" },
        };

        PublicationDecision gold = Plan(Ordinary(), slots, StreamPcSettings(), rank: "Gold");
        PublicationDecision silver = Plan(Ordinary(), slots, StreamPcSettings(), rank: "Silver");

        Assert.Equal(Now.AddHours(2), gold.PublishAtUtc);
        Assert.Equal(Now.AddHours(1), silver.PublishAtUtc);
    }

    /// <summary>
    /// Production on 2026-10-17: the roster check pooled the heroes of every video within 8 h,
    /// which at 24 a day covers most of the roster, so no waiting recording found a slot. It now
    /// compares each nearby video on its own.
    /// </summary>
    [Fact]
    public void Plan_ChecksRosterOverlapAgainstEachVideo_NotTheirPooledHeroes()
    {
        string[] roster = { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J" };
        var pooled = new[]
        {
            new PublicationSample { At = Now.AddHours(1), Heroes = new[] { "A", "B", "X1", "X2" } },
            new PublicationSample { At = Now.AddHours(2), Heroes = new[] { "C", "D", "X3", "X4" } },
        };
        var one = new[]
        {
            new PublicationSample { At = Now.AddHours(1), Heroes = new[] { "A", "B", "C", "D" } },
        };

        PublicationDecision spread = PublicationSchedule.Plan(
            StreamPcSettings(),
            Ordinary(),
            true,
            0,
            Now,
            pooled,
            null,
            null,
            null,
            roster
        );
        PublicationDecision repeat = PublicationSchedule.Plan(
            StreamPcSettings(),
            Ordinary(),
            true,
            0,
            Now,
            one,
            null,
            null,
            null,
            roster
        );

        Assert.Equal(Now, spread.PublishAtUtc);
        Assert.Equal(Now.AddHours(9), repeat.PublishAtUtc);
    }

    [Fact]
    public void Plan_PacesAFocusVideoWithHalfTheMapAndRankCooldowns_ButTheWholeHeroCooldown()
    {
        var slots = new[]
        {
            new PublicationSample
            {
                At = Now,
                Map = "Alterac Pass",
                Rank = "Gold",
                Hero = "Li-Ming",
            },
        };
        var focus = new PublicationSendFacts
        {
            Criteria = ReplayMediaPriority.Focus,
            RecordedAtUtc = Now,
        };

        PublicationDecision ordinaryMap = Plan(
            Ordinary(),
            slots,
            StreamPcSettings(),
            map: "Alterac Pass"
        );
        PublicationDecision focusMap = Plan(focus, slots, StreamPcSettings(), map: "Alterac Pass");
        PublicationDecision focusRank = Plan(focus, slots, StreamPcSettings(), rank: "Gold");
        PublicationDecision focusHero = Plan(focus, slots, StreamPcSettings(), hero: "Li-Ming");

        Assert.Equal(Now.AddHours(8), ordinaryMap.PublishAtUtc);
        Assert.Equal(Now.AddHours(4), focusMap.PublishAtUtc);
        Assert.Equal(Now.AddHours(1), focusRank.PublishAtUtc);
        Assert.Equal(Now.AddHours(8), focusHero.PublishAtUtc);
    }

    /// <summary>
    /// #507: the stream plays about 60 games a day. A day of them, one every 24 minutes over the
    /// 12 Storm League maps and 6 tiers, publishes as each one is uploaded at the production pace
    /// since then. At the pace before it (24 a day, an hour apart) the same day's last game would
    /// go public about two days after its upload, and the queue behind it kept growing.
    /// </summary>
    [Fact]
    public void SupplyPace_PublishesADayOfGamesAsTheyAreUploaded()
    {
        Assert.Equal(TimeSpan.Zero, LongestWait(SupplyPaceSettings()));
        Assert.True(LongestWait(StreamPcSettings()) > TimeSpan.FromDays(1.5));
    }

    /// <summary>The longest time from upload to publish over 60 games played 24 minutes apart.</summary>
    private static TimeSpan LongestWait(ReplayMediaPolicySettings settings)
    {
        string[] maps =
        {
            "Alterac Pass",
            "Battlefield of Eternity",
            "Braxis Holdout",
            "Cursed Hollow",
            "Dragon Shire",
            "Garden of Terror",
            "Haunted Mines",
            "Infernal Shrines",
            "Sky Temple",
            "Tomb of the Spider Queen",
            "Towers of Doom",
            "Volskaya Foundry",
        };
        string[] tiers = { "Bronze 1", "Silver 2", "Gold 3", "Platinum 4", "Diamond 5", "Master" };
        var slots = new List<PublicationSample>();
        TimeSpan longest = TimeSpan.Zero;
        for (int game = 0; game < 60; game++)
        {
            DateTimeOffset uploaded = Now + TimeSpan.FromMinutes(24 * game);
            string map = maps[game % maps.Length];
            string rank = tiers[game % tiers.Length];
            PublicationDecision decision = PublicationSchedule.Plan(
                settings,
                new PublicationSendFacts
                {
                    Criteria = ReplayMediaPriority.Ordinary,
                    RecordedAtUtc = uploaded,
                },
                true,
                0,
                uploaded,
                slots,
                map,
                rank,
                null,
                null
            );
            Assert.True(decision.Allow, $"game {game}: {decision.Reason}");
            DateTimeOffset at = decision.PublishAtUtc.GetValueOrDefault();
            slots.Add(
                new PublicationSample
                {
                    At = at,
                    Map = map,
                    Rank = rank,
                }
            );
            if (at - uploaded > longest)
            {
                longest = at - uploaded;
            }
        }

        return longest;
    }

    /// <summary>
    /// <c>appsettings.prod.json</c> since #507: 72 a day, 20 minutes apart, 2 h map, 1 h rank and
    /// 4 h hero cooldowns, 95 inserts.
    /// </summary>
    private static ReplayMediaPolicySettings SupplyPaceSettings()
    {
        ReplayMediaPolicySettings settings = StreamPcSettings();
        settings.MaxPublicPerDay = 72;
        settings.MaxPublicPerWeek = 504;
        settings.MinimumPublicInterval = TimeSpan.FromMinutes(20);
        settings.MapCooldown = TimeSpan.FromHours(2);
        settings.RankCooldown = TimeSpan.FromHours(1);
        settings.FeaturedHeroCooldown = TimeSpan.FromHours(4);
        settings.MaxInsertsPerQuotaDay = 95;
        settings.MaxWaitingRecordings = 6;
        return settings;
    }

    /// <summary><c>appsettings.prod.json</c> from 2026-10-17 to #507 (the rank cooldown was 4 h from 2026-10-13).</summary>
    private static ReplayMediaPolicySettings StreamPcSettings() =>
        new()
        {
            Version = "1",
            RecordingMode = ReplayRecordingMode.Selected,
            PublicationMode = ReplayPublicationMode.AllEligible,
            MaxPublicPerDay = 24,
            MaxPublicPerWeek = 168,
            MinimumPublicInterval = TimeSpan.FromHours(1),
            MaxPublishAhead = TimeSpan.FromDays(3),
            OrdinaryCandidateMaxAge = TimeSpan.FromDays(3),
            MapCooldown = TimeSpan.FromHours(8),
            RankCooldown = TimeSpan.FromHours(2),
            FeaturedHeroCooldown = TimeSpan.FromHours(8),
            MaxSharedHeroes = 4,
            ReservedRequestSlotsPerDay = 2,
            MaxInsertsPerQuotaDay = 30,
        };

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
