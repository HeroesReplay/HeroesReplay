using System;
using System.Collections.Generic;
using HeroesReplay.Core.Services.YouTube;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PublicationScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Decide_DeniesTheQuotaCapOnEveryHost()
    {
        PublicationDecision capped = Decide(
            production: false,
            inserts: PublicationSchedule.MaxInsertsPerQuotaDay
        );
        PublicationDecision under = Decide(
            production: false,
            inserts: PublicationSchedule.MaxInsertsPerQuotaDay - 1
        );

        Assert.False(capped.Allow);
        Assert.Equal("quota", capped.Reason);
        Assert.True(under.Allow);
        Assert.Equal("host", under.Reason);
    }

    [Fact]
    public void Decide_DeniesTheWeekAtThirtyAndAllowsTwentyNine()
    {
        PublicationDecision full = Decide(publicAt: Week(30), requested: true);
        PublicationDecision room = Decide(publicAt: Week(29), requested: true);

        Assert.False(full.Allow);
        Assert.Equal("week", full.Reason);
        Assert.True(room.Allow);
        Assert.Equal("ready", room.Reason);
    }

    [Fact]
    public void Decide_DeniesTheDayAtSix()
    {
        PublicationDecision full = Decide(publicAt: Recent(6), requested: true);
        PublicationDecision room = Decide(publicAt: Recent(5), requested: true);

        Assert.False(full.Allow);
        Assert.Equal("day", full.Reason);
        Assert.True(room.Allow);
        Assert.Equal("ready", room.Reason);
    }

    [Fact]
    public void Decide_ReservesTwoDailySlotsForRequests()
    {
        PublicationDecision ordinaryFull = Decide(publicAt: Recent(4), requested: false);
        PublicationDecision ordinaryRoom = Decide(publicAt: Recent(3), requested: false);
        PublicationDecision requestStillFits = Decide(
            publicAt: Recent(4),
            requested: true,
            requestedInDay: 1
        );
        PublicationDecision requestsFull = Decide(
            publicAt: Recent(4),
            requested: true,
            requestedInDay: PublicationSchedule.ReservedRequestSlotsPerDay
        );
        PublicationDecision requestBeforeTheDayCap = Decide(
            publicAt: Recent(5),
            requested: true,
            requestedInDay: 1
        );

        Assert.False(ordinaryFull.Allow);
        Assert.Equal("reserved", ordinaryFull.Reason);
        Assert.True(ordinaryRoom.Allow);
        Assert.True(requestStillFits.Allow);
        Assert.Equal("ready", requestStillFits.Reason);
        Assert.False(requestsFull.Allow);
        Assert.Equal("reserved", requestsFull.Reason);
        Assert.True(requestBeforeTheDayCap.Allow);
    }

    [Fact]
    public void Decide_DeniesAProductionIntervalUnderTwoHours()
    {
        PublicationDecision early = Decide(lastPublic: Now.AddHours(-2).AddTicks(1));
        PublicationDecision ready = Decide(
            lastPublic: Now.Add(-PublicationSchedule.MinimumInterval)
        );
        PublicationDecision backwards = Decide(lastPublic: Now.AddMinutes(1));

        Assert.False(early.Allow);
        Assert.Equal("interval", early.Reason);
        Assert.True(ready.Allow);
        Assert.False(backwards.Allow);
        Assert.Equal("interval", backwards.Reason);
    }

    [Fact]
    public void Decide_DeniesAnOrdinaryReplayOlderThanSeventyTwoHours()
    {
        PublicationDecision stale = Decide(
            requested: false,
            recordedAt: Now.Add(-PublicationSchedule.OrdinaryMaxAge).AddTicks(-1)
        );
        PublicationDecision fresh = Decide(
            requested: false,
            recordedAt: Now.Add(-PublicationSchedule.OrdinaryMaxAge)
        );
        PublicationDecision requested = Decide(requested: true, recordedAt: Now.AddDays(-30));

        Assert.False(stale.Allow);
        Assert.Equal("stale", stale.Reason);
        Assert.True(fresh.Allow);
        Assert.True(requested.Allow);
    }

    [Fact]
    public void Decide_PenalizesARepeatedMapOrHeroAndStillAllowsIt()
    {
        PublicationDecision map = Decide(
            map: "Tomb of the Spider Queen",
            lastMap: " tomb of the spider queen ",
            lastMapAt: Now.AddHours(-8).AddTicks(1)
        );
        PublicationDecision cooled = Decide(
            map: "Tomb of the Spider Queen",
            lastMap: "Tomb of the Spider Queen",
            lastMapAt: Now.Add(-PublicationSchedule.DiversityCooldown)
        );
        PublicationDecision both = Decide(
            map: "Alterac",
            lastMap: "Alterac",
            lastMapAt: Now.AddHours(-1),
            hero: "Li-Ming",
            lastHero: "li-ming",
            lastHeroAt: Now.AddHours(-1)
        );

        Assert.True(map.Allow);
        Assert.Equal("cooldown", map.Reason);
        Assert.Equal(1, map.Penalty);
        Assert.True(cooled.Allow);
        Assert.Equal("ready", cooled.Reason);
        Assert.Equal(0, cooled.Penalty);
        Assert.True(both.Allow);
        Assert.Equal(2, both.Penalty);
    }

    [Fact]
    public void Decide_LetsANonProductionHostIgnoreThePublicBudgets()
    {
        PublicationDecision host = Decide(production: false, publicAt: Recent(40));

        Assert.True(host.Allow);
        Assert.Equal("host", host.Reason);
    }

    [Fact]
    public void NextPublishAt_InsertsPrivateAndSchedulesTheNextPublicTime()
    {
        DateTimeOffset recent = Now.AddHours(-1);
        DateTimeOffset spaced = Now.Add(-PublicationSchedule.MinimumInterval).AddHours(-1);
        DateTimeOffset local = new(2026, 9, 30, 19, 0, 0, TimeSpan.FromHours(1));

        Assert.Equal("private", UploadVisibility.InsertStatus("public"));
        Assert.Null(PublicationSchedule.NextPublishAt("private", Now, null));
        Assert.Equal(Now, PublicationSchedule.NextPublishAt("public", Now, null));
        Assert.Equal(
            recent.Add(PublicationSchedule.MinimumInterval),
            PublicationSchedule.NextPublishAt("public", Now, recent)
        );
        Assert.Equal(Now, PublicationSchedule.NextPublishAt("public", Now, spaced));
        Assert.Null(PublicationSchedule.NextPublishAt("public", local, null));
        Assert.Equal(
            recent.Add(PublicationSchedule.MinimumInterval),
            UploadVisibility.PublishAt(
                "public",
                PublicationSchedule.NextPublishAt("public", Now, recent)
            )
        );
    }

    [Fact]
    public void PublishedIn_CountsOnlyTimesInsideTheWindow()
    {
        var times = new List<DateTimeOffset>
        {
            Now.AddHours(-23),
            Now.AddHours(-25),
            Now.AddMinutes(1),
        };

        Assert.Equal(1, PublicationSchedule.PublishedIn(times, Now, TimeSpan.FromHours(24)));
        Assert.Equal(0, PublicationSchedule.PublishedIn(null, Now, TimeSpan.FromHours(24)));
    }

    private static PublicationDecision Decide(
        bool production = true,
        int inserts = 0,
        DateTimeOffset? lastPublic = null,
        IReadOnlyList<DateTimeOffset> publicAt = null,
        int requestedInDay = 0,
        bool requested = false,
        DateTimeOffset? recordedAt = null,
        string map = null,
        string lastMap = null,
        DateTimeOffset? lastMapAt = null,
        string hero = null,
        string lastHero = null,
        DateTimeOffset? lastHeroAt = null
    )
    {
        return PublicationSchedule.Decide(
            production,
            inserts,
            Now,
            lastPublic,
            publicAt,
            requestedInDay,
            requested,
            recordedAt,
            map,
            lastMap,
            lastMapAt,
            hero,
            lastHero,
            lastHeroAt
        );
    }

    private static List<DateTimeOffset> Recent(int count)
    {
        var times = new List<DateTimeOffset>();
        for (int i = 0; i < count; i++)
        {
            times.Add(Now.AddMinutes(-i));
        }

        return times;
    }

    private static List<DateTimeOffset> Week(int count)
    {
        var times = new List<DateTimeOffset> { Now.AddHours(-1) };
        for (int i = 1; i < count; i++)
        {
            times.Add(Now.AddDays(-2).AddMinutes(-i));
        }

        return times;
    }
}
