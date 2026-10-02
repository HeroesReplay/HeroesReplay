using System;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.YouTube.Publication;
using Xunit;

namespace HeroesReplay.Tests.Unit.MediaPolicy;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PublicationAdmitTests
{
    [Fact]
    public void Decide_WithholdsWhenThereIsNoCandidate()
    {
        PublicationAdmitResult missing = PublicationAdmit.Decide(null, 0);
        PublicationAdmitResult refused = PublicationAdmit.Decide(
            Decision(ReplayPublicationMode.Curated, ReplayMediaPriority.Notable, false, null),
            0
        );

        Assert.False(missing.Allow);
        Assert.Equal(ReplayMediaReason.NotSelected, missing.Reason);
        Assert.False(refused.Allow);
    }

    [Fact]
    public void Decide_WithholdsADisabledMode()
    {
        PublicationAdmitResult result = PublicationAdmit.Decide(
            Decision(
                ReplayPublicationMode.Disabled,
                ReplayMediaPriority.Requested,
                true,
                ReplayMediaReason.EligibleRequested
            ),
            0
        );

        Assert.False(result.Allow);
        Assert.Equal(ReplayMediaReason.PublicationDisabled, result.Reason);
    }

    [Fact]
    public void Decide_AdmitsRequestedAndAllEligibleWhenTheDayIsFull()
    {
        int full = PublicationSchedule.MaxPublicPerDay;
        PublicationAdmitResult requested = PublicationAdmit.Decide(
            Decision(
                ReplayPublicationMode.Curated,
                ReplayMediaPriority.Requested,
                true,
                ReplayMediaReason.EligibleRequested
            ),
            full
        );
        PublicationAdmitResult all = PublicationAdmit.Decide(
            Decision(
                ReplayPublicationMode.AllEligible,
                ReplayMediaPriority.Ordinary,
                true,
                ReplayMediaReason.EligibleAll
            ),
            full
        );

        Assert.True(requested.Allow);
        Assert.Equal(ReplayMediaReason.EligibleRequested, requested.Reason);
        Assert.True(all.Allow);
        Assert.Equal(ReplayMediaReason.EligibleAll, all.Reason);
    }

    [Fact]
    public void Decide_WithholdsAnOrdinaryAllEligibleCandidateOlderThanThreeDays()
    {
        DateTime played = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        PublicationAdmitResult stale = PublicationAdmit.Decide(
            Decision(
                ReplayPublicationMode.AllEligible,
                ReplayMediaPriority.Ordinary,
                true,
                ReplayMediaReason.EligibleAll,
                played,
                played + PublicationSchedule.OrdinaryMaxAge + TimeSpan.FromTicks(1)
            ),
            0
        );
        PublicationAdmitResult atLimit = PublicationAdmit.Decide(
            Decision(
                ReplayPublicationMode.AllEligible,
                ReplayMediaPriority.Ordinary,
                true,
                ReplayMediaReason.EligibleAll,
                played,
                played + PublicationSchedule.OrdinaryMaxAge
            ),
            0
        );
        PublicationAdmitResult requested = PublicationAdmit.Decide(
            Decision(
                ReplayPublicationMode.AllEligible,
                ReplayMediaPriority.Requested,
                true,
                ReplayMediaReason.EligibleRequested,
                played,
                played + PublicationSchedule.OrdinaryMaxAge + TimeSpan.FromDays(1)
            ),
            0
        );

        Assert.False(stale.Allow);
        Assert.Equal(ReplayMediaReason.Expired, stale.Reason);
        Assert.True(atLimit.Allow);
        Assert.Equal(ReplayMediaReason.EligibleAll, atLimit.Reason);
        Assert.True(requested.Allow);
        Assert.Equal(ReplayMediaReason.EligibleRequested, requested.Reason);
    }

    [Fact]
    public void Decide_WithholdsCuratedOrdinary()
    {
        PublicationAdmitResult result = PublicationAdmit.Decide(
            Decision(
                ReplayPublicationMode.Curated,
                ReplayMediaPriority.Ordinary,
                true,
                ReplayMediaReason.EligibleCurated
            ),
            0
        );

        Assert.False(result.Allow);
        Assert.Equal(ReplayMediaReason.NotSelected, result.Reason);
    }

    [Theory]
    [InlineData(ReplayMediaPriority.Notable)]
    [InlineData(ReplayMediaPriority.HighSkill)]
    public void Decide_AdmitsCuratedNotableAndHighSkillAndSchedulesAFullDay(
        ReplayMediaPriority priority
    )
    {
        PublicationAdmitResult under = PublicationAdmit.Decide(
            Decision(
                ReplayPublicationMode.Curated,
                priority,
                true,
                ReplayMediaReason.EligibleCurated
            ),
            PublicationSchedule.MaxPublicPerDay - 1
        );
        PublicationAdmitResult atCap = PublicationAdmit.Decide(
            Decision(
                ReplayPublicationMode.Curated,
                priority,
                true,
                ReplayMediaReason.EligibleCurated
            ),
            PublicationSchedule.MaxPublicPerDay
        );

        PublicationAdmitResult atCapWithoutScheduling = PublicationAdmit.Decide(
            Decision(
                ReplayPublicationMode.Curated,
                priority,
                true,
                ReplayMediaReason.EligibleCurated
            ),
            PublicationSchedule.MaxPublicPerDay,
            new ReplayMediaPolicySettings
            {
                RecordingMode = ReplayRecordingMode.All,
                PublicationMode = ReplayPublicationMode.Curated,
                MaxPublishAhead = TimeSpan.Zero,
            }
        );

        Assert.True(under.Allow);
        Assert.Equal(ReplayMediaReason.EligibleCurated, under.Reason);
        Assert.True(atCap.Allow);
        Assert.Equal(ReplayMediaReason.EligibleCurated, atCap.Reason);
        Assert.False(atCapWithoutScheduling.Allow);
        Assert.Equal(ReplayMediaReason.NotSelected, atCapWithoutScheduling.Reason);
    }

    [Fact]
    public void Decide_WithholdsAnOrdinaryRequestedOnlyCandidate()
    {
        PublicationAdmitResult result = PublicationAdmit.Decide(
            Decision(
                ReplayPublicationMode.RequestedOnly,
                ReplayMediaPriority.Ordinary,
                true,
                ReplayMediaReason.EligibleCurated
            ),
            0
        );

        Assert.False(result.Allow);
        Assert.Equal(ReplayMediaReason.NotRequested, result.Reason);
    }

    private static ReplayMediaDecision Decision(
        ReplayPublicationMode mode,
        ReplayMediaPriority priority,
        bool candidate,
        string reason,
        DateTime? gameDateUtc = null,
        DateTime? evaluatedAtUtc = null
    )
    {
        return new ReplayMediaDecision
        {
            PublicationCandidate = candidate,
            PublicationMode = mode,
            Priority = priority,
            PublicationReason = reason,
            SchedulerCurationRequired = candidate && mode == ReplayPublicationMode.Curated,
            GameDateUtc = gameDateUtc,
            EvaluatedAtUtc = evaluatedAtUtc ?? default,
        };
    }
}
