using System;
using System.Collections.Generic;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.MediaPolicy;
using Xunit;

namespace HeroesReplay.Tests.Unit.MediaPolicy;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayMediaPolicyTests
{
    private static readonly DateTime GameDate = new DateTime(
        2026,
        9,
        28,
        18,
        0,
        0,
        DateTimeKind.Utc
    );
    private static readonly DateTime Now = new DateTime(2026, 9, 29, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IdenticalInput_ProducesTheSameDecisionAndScore()
    {
        ReplayMediaPolicyInput input = Sample(events: new[] { Pentakill("Li-Ming", 30) });
        ReplayMediaPolicySettings settings = Settings();

        ReplayMediaDecision first = ReplayMediaPolicy.Evaluate(input, settings, Now);
        ReplayMediaDecision second = ReplayMediaPolicy.Evaluate(input, settings, Now);

        AssertSame(first, second);
        Assert.Equal(ReplayMediaPolicy.PolicyVersion, first.PolicyVersion);
        Assert.Equal("1", first.ConfigurationVersion);
        Assert.Equal(
            first.Score.PriorityWeight
                + first.Score.Recency
                + first.Score.NotableStrength
                + first.Score.Skill,
            first.Score.Total
        );
        Assert.Equal(GameDate.AddDays(7), first.CandidateExpiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, first.CandidateExpiresAtUtc.Value.Kind);
    }

    [Fact]
    public void UnspecifiedClock_MatchesUtcClock()
    {
        DateTime utc = new DateTime(2026, 9, 29, 18, 0, 0, DateTimeKind.Utc);
        DateTime unspecified = new DateTime(2026, 9, 29, 18, 0, 0, DateTimeKind.Unspecified);

        ReplayMediaDecision fromUtc = Decide(now: utc);
        ReplayMediaDecision fromUnspecified = Decide(now: unspecified);

        Assert.Equal(DateTimeKind.Utc, fromUnspecified.EvaluatedAtUtc.Kind);
        Assert.Equal(fromUtc.EvaluatedAtUtc.Ticks, fromUnspecified.EvaluatedAtUtc.Ticks);
        Assert.Equal(fromUtc.Score, fromUnspecified.Score);
        Assert.Equal(fromUtc.PublicationReason, fromUnspecified.PublicationReason);
        Assert.Equal(fromUtc.CandidateExpiresAtUtc, fromUnspecified.CandidateExpiresAtUtc);
    }

    [Fact]
    public void SuppliedClock_ChangesRecencyWithoutMovingExpiry()
    {
        ReplayMediaDecision earlier = Decide(now: GameDate.AddDays(1));
        ReplayMediaDecision later = Decide(now: GameDate.AddDays(2));

        Assert.Equal(GameDate.AddDays(3), earlier.CandidateExpiresAtUtc);
        Assert.Equal(earlier.CandidateExpiresAtUtc, later.CandidateExpiresAtUtc);
        Assert.True(earlier.Score.Recency > later.Score.Recency);
        Assert.True(earlier.PublicationCandidate);
        Assert.True(later.PublicationCandidate);
    }

    [Fact]
    public void Recency_DropsOnWholeMinutes()
    {
        ReplayMediaDecision fresh = Decide(now: GameDate);
        ReplayMediaDecision almost = Decide(now: GameDate.AddSeconds(59));
        ReplayMediaDecision minute = Decide(now: GameDate.AddMinutes(1));

        Assert.Equal(ReplayMediaPolicy.RecencyHorizonMinutes, fresh.Score.Recency);
        Assert.Equal(ReplayMediaPolicy.RecencyHorizonMinutes, almost.Score.Recency);
        Assert.Equal(ReplayMediaPolicy.RecencyHorizonMinutes - 1, minute.Score.Recency);
    }

    [Fact]
    public void FutureGameDate_IsNotExpired()
    {
        DateTime future = Now.AddDays(1);
        ReplayMediaDecision decision = Decide(Sample(gameDate: future));

        Assert.Equal(ReplayMediaPolicy.RecencyHorizonMinutes, decision.Score.Recency);
        Assert.True(decision.PublicationCandidate);
        Assert.Equal(future.AddDays(3), decision.CandidateExpiresAtUtc);
    }

    [Fact]
    public void Expiry_AtExactOrdinaryBoundary_IsStillEligible()
    {
        ReplayMediaDecision decision = Decide(now: GameDate.AddDays(3));

        Assert.True(decision.Record);
        Assert.True(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.RecordedOrdinary, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.EligibleCurated, decision.PublicationReason);
    }

    [Fact]
    public void Expiry_OneTickPast_IsNotACandidate()
    {
        DateTime late = GameDate.AddDays(3).AddTicks(1);
        ReplayMediaDecision curated = Decide(now: late);
        ReplayMediaDecision allEligible = Decide(
            settings: Settings(publication: ReplayPublicationMode.AllEligible),
            now: late
        );

        Assert.False(curated.Record);
        Assert.False(curated.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.Expired, curated.RecordingReason);
        Assert.Equal(ReplayMediaReason.Expired, curated.PublicationReason);
        Assert.False(allEligible.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.Expired, allEligible.PublicationReason);
        Assert.NotEqual(ReplayMediaReason.EligibleAll, allEligible.PublicationReason);
    }

    [Fact]
    public void ZeroMaxAge_ExpiresOnTheNextTick()
    {
        ReplayMediaPolicySettings settings = Settings();
        settings.OrdinaryCandidateMaxAge = TimeSpan.Zero;

        ReplayMediaDecision onTime = Decide(settings: settings, now: GameDate);
        ReplayMediaDecision late = Decide(settings: settings, now: GameDate.AddTicks(1));

        Assert.True(onTime.PublicationCandidate);
        Assert.False(late.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.Expired, late.PublicationReason);
        Assert.Equal(GameDate, onTime.CandidateExpiresAtUtc);
    }

    [Fact]
    public void All_RecordsExpiredReplay_ButDoesNotPublishIt()
    {
        ReplayMediaDecision decision = Decide(
            settings: Settings(recording: ReplayRecordingMode.All),
            now: GameDate.AddDays(3).AddTicks(1)
        );

        Assert.True(decision.Record);
        Assert.Equal(ReplayMediaReason.RecordedAll, decision.RecordingReason);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.Expired, decision.PublicationReason);
    }

    [Fact]
    public void RequestedExpiry_IsLongerThanOrdinary()
    {
        DateTime played = Now.AddDays(-10);
        ReplayMediaDecision ordinary = Decide(Sample(gameDate: played));
        ReplayMediaDecision requested = Decide(Sample(gameDate: played, recordAndUpload: true));

        Assert.Equal(ReplayMediaReason.Expired, ordinary.PublicationReason);
        Assert.False(ordinary.Record);
        Assert.Equal(ReplayMediaReason.EligibleRequested, requested.PublicationReason);
        Assert.True(requested.Record);
        Assert.Equal(ReplayMediaPriority.Requested, requested.Priority);
        Assert.Equal(played.AddDays(14), requested.CandidateExpiresAtUtc);
        Assert.Equal(played.AddDays(3), ordinary.CandidateExpiresAtUtc);
    }

    [Theory]
    [InlineData("2.57.0.98285", true)]
    [InlineData(" 2.57.0.98285 ", true)]
    [InlineData("2.57.0.98286", true)]
    [InlineData("2.58", true)]
    [InlineData("2.57.0.98284", false)]
    [InlineData("2.56.17.98025", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void CurrentPatch_Boundary(string version, bool allowed)
    {
        ReplayMediaDecision decision = Decide(Sample(version: version));

        if (allowed)
        {
            Assert.True(decision.Record);
            Assert.True(decision.PublicationCandidate);
            Assert.Equal(ReplayMediaPriority.Ordinary, decision.Priority);
            Assert.Equal(ReplayMediaReason.RecordedOrdinary, decision.RecordingReason);
            Assert.Equal(ReplayMediaReason.EligibleCurated, decision.PublicationReason);
        }
        else
        {
            Assert.False(decision.Record);
            Assert.False(decision.PublicationCandidate);
            Assert.Equal(ReplayMediaReason.OffPatch, decision.RecordingReason);
            Assert.Equal(ReplayMediaReason.OffPatch, decision.PublicationReason);
        }
    }

    [Fact]
    public void PatchRequirement_CanBeDisabled()
    {
        ReplayMediaPolicySettings settings = Settings();
        settings.RequireCurrentPatch = false;

        ReplayMediaDecision decision = Decide(Sample(version: "1.0.0.1"), settings);

        Assert.True(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.EligibleCurated, decision.PublicationReason);
    }

    [Fact]
    public void RequestedReplay_BypassesPatch_OnlyWhenConfigured()
    {
        ReplayMediaPolicyInput paid = Sample(version: "1.0.0.1", recordAndUpload: true);
        ReplayMediaDecision allowed = Decide(paid);
        ReplayMediaPolicySettings blocked = Settings();
        blocked.RequestsBypassPatchRequirement = false;
        ReplayMediaDecision denied = Decide(paid, blocked);

        Assert.True(allowed.Record);
        Assert.True(allowed.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.RecordedRequested, allowed.RecordingReason);
        Assert.Equal(ReplayMediaReason.EligibleRequested, allowed.PublicationReason);
        Assert.False(denied.Record);
        Assert.False(denied.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.OffPatch, denied.RecordingReason);
        Assert.Equal(ReplayMediaReason.OffPatch, denied.PublicationReason);
        Assert.Equal(ReplayMediaPriority.Requested, denied.Priority);
    }

    [Fact]
    public void All_RecordsOffPatch_ButAllEligibleDoesNotPublishIt()
    {
        ReplayMediaDecision decision = Decide(
            Sample(version: "1.0.0.1"),
            Settings(
                recording: ReplayRecordingMode.All,
                publication: ReplayPublicationMode.AllEligible
            )
        );

        Assert.True(decision.Record);
        Assert.Equal(ReplayMediaReason.RecordedAll, decision.RecordingReason);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.OffPatch, decision.PublicationReason);
        Assert.NotEqual(ReplayMediaReason.EligibleAll, decision.PublicationReason);
    }

    [Fact]
    public void All_RecordsOnPatchReplay()
    {
        ReplayMediaDecision decision = Decide(
            settings: Settings(recording: ReplayRecordingMode.All)
        );

        Assert.True(decision.Record);
        Assert.Equal(ReplayMediaReason.RecordedAll, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.EligibleCurated, decision.PublicationReason);
        Assert.True(decision.SchedulerCurationRequired);
    }

    [Fact]
    public void AllEligible_IsDistinctFromCurated()
    {
        ReplayMediaDecision curated = Decide();
        ReplayMediaDecision allEligible = Decide(
            settings: Settings(publication: ReplayPublicationMode.AllEligible)
        );

        Assert.True(curated.PublicationCandidate);
        Assert.True(allEligible.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.EligibleCurated, curated.PublicationReason);
        Assert.Equal(ReplayMediaReason.EligibleAll, allEligible.PublicationReason);
        Assert.True(curated.SchedulerCurationRequired);
        Assert.False(allEligible.SchedulerCurationRequired);
        Assert.Equal(ReplayPublicationMode.Curated, curated.PublicationMode);
        Assert.Equal(ReplayPublicationMode.AllEligible, allEligible.PublicationMode);
    }

    [Fact]
    public void AllEligible_DoesNotBypassCompletion()
    {
        ReplayMediaDecision decision = Decide(
            Sample(includeCompletion: false),
            Settings(publication: ReplayPublicationMode.AllEligible)
        );

        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.AwaitingCompletion, decision.PublicationReason);
    }

    [Fact]
    public void DefaultSettings_RecordNothingAndPublishNothing()
    {
        ReplayMediaDecision decision = Decide(settings: new ReplayMediaPolicySettings());

        Assert.Empty(decision.ConfigurationErrors);
        Assert.False(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.RecordingDisabled, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.PublicationDisabled, decision.PublicationReason);
        Assert.Equal(ReplayMediaPolicy.PolicyVersion, decision.PolicyVersion);
    }

    [Fact]
    public void All_RecordsWhenPublicationIsDisabled()
    {
        ReplayMediaDecision decision = Decide(
            settings: Settings(
                recording: ReplayRecordingMode.All,
                publication: ReplayPublicationMode.Disabled
            )
        );

        Assert.Empty(decision.ConfigurationErrors);
        Assert.True(decision.Record);
        Assert.Equal(ReplayMediaReason.RecordedAll, decision.RecordingReason);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.PublicationDisabled, decision.PublicationReason);
    }

    [Fact]
    public void RequestedOnlyRecording_DoesNotCaptureOrdinaryReplays()
    {
        ReplayMediaDecision decision = Decide(
            settings: Settings(recording: ReplayRecordingMode.RequestedOnly)
        );

        Assert.False(decision.Record);
        Assert.Equal(ReplayMediaReason.NotRequested, decision.RecordingReason);
        Assert.True(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.EligibleCurated, decision.PublicationReason);
    }

    [Fact]
    public void RequestedOnlyRecording_LeavesNotablePublicationToCurated()
    {
        ReplayMediaDecision decision = Decide(
            Sample(events: new[] { Pentakill("Greymane", 40) }),
            Settings(recording: ReplayRecordingMode.RequestedOnly)
        );

        Assert.False(decision.Record);
        Assert.Equal(ReplayMediaReason.NotRequested, decision.RecordingReason);
        Assert.Equal(ReplayMediaPriority.Notable, decision.Priority);
        Assert.True(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.EligibleCurated, decision.PublicationReason);
    }

    [Fact]
    public void PublicationRequestedOnly_RejectsNotableButSelectedStillRecords()
    {
        ReplayMediaDecision decision = Decide(
            Sample(events: new[] { Wipe("Artanis", 12) }),
            Settings(
                recording: ReplayRecordingMode.Selected,
                publication: ReplayPublicationMode.RequestedOnly
            )
        );

        Assert.True(decision.Record);
        Assert.Equal(ReplayMediaReason.RecordedNotable, decision.RecordingReason);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.NotRequested, decision.PublicationReason);
    }

    [Fact]
    public void RequestedOnly_DoesNotPromoteHighSkill()
    {
        ReplayMediaDecision decision = Decide(
            Sample(rank: "Master", mmr: 1000),
            Settings(
                recording: ReplayRecordingMode.Selected,
                publication: ReplayPublicationMode.RequestedOnly
            )
        );

        Assert.Equal(ReplayMediaPriority.HighSkill, decision.Priority);
        Assert.False(decision.Record);
        Assert.Equal(ReplayMediaReason.NotSelected, decision.RecordingReason);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.NotRequested, decision.PublicationReason);
    }

    [Fact]
    public void Selected_RecordsNotableWhenPublicationIsDisabled()
    {
        ReplayMediaDecision decision = Decide(
            Sample(events: new[] { Pentakill("Greymane", 8) }),
            Settings(
                recording: ReplayRecordingMode.Selected,
                publication: ReplayPublicationMode.Disabled
            )
        );

        Assert.Empty(decision.ConfigurationErrors);
        Assert.True(decision.Record);
        Assert.Equal(ReplayMediaReason.RecordedNotable, decision.RecordingReason);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.PublicationDisabled, decision.PublicationReason);
    }

    [Fact]
    public void Selected_SkipsOrdinaryWhenPublicationIsDisabled()
    {
        ReplayMediaDecision decision = Decide(
            settings: Settings(
                recording: ReplayRecordingMode.Selected,
                publication: ReplayPublicationMode.Disabled
            )
        );

        Assert.False(decision.Record);
        Assert.Equal(ReplayMediaReason.NotSelected, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.PublicationDisabled, decision.PublicationReason);
    }

    [Fact]
    public void PaidRequest_OutranksNotableHighSkillAndOrdinary()
    {
        ReplayMediaPolicySettings settings = Settings();
        settings.OrdinaryCandidateMaxAge = TimeSpan.FromDays(30);
        settings.HighSkillCandidateMaxAge = TimeSpan.FromDays(30);
        settings.NotableCandidateMaxAge = TimeSpan.FromDays(30);
        settings.RequestedCandidateMaxAge = TimeSpan.FromDays(30);
        var crowded = new List<TeamKillClip>();
        for (int second = 0; second < 300; second++)
        {
            crowded.Add(Pentakill("Greymane", second));
        }

        ReplayMediaDecision requested = Decide(
            Sample(rank: "Bronze", mmr: 1000, recordAndUpload: true, gameDate: Now.AddDays(-20)),
            settings
        );
        ReplayMediaDecision notable = Decide(
            Sample(
                rank: "Grandmaster",
                mmr: 22800,
                events: crowded,
                gameDate: Now,
                focusHero: "Greymane"
            ),
            settings
        );
        ReplayMediaDecision highSkill = Decide(
            Sample(rank: "Grandmaster", mmr: 22800, gameDate: Now),
            settings
        );
        ReplayMediaDecision ordinary = Decide(
            Sample(rank: "Gold", mmr: 1500, gameDate: Now),
            settings
        );

        Assert.Equal(ReplayMediaPriority.Requested, requested.Priority);
        Assert.Equal(ReplayMediaPriority.Notable, notable.Priority);
        Assert.Equal(ReplayMediaPriority.HighSkill, highSkill.Priority);
        Assert.Equal(ReplayMediaPriority.Ordinary, ordinary.Priority);
        Assert.Equal(ReplayMediaPolicy.RequestedScore, requested.Score.PriorityWeight);
        Assert.Equal(ReplayMediaPolicy.NotableScore, notable.Score.PriorityWeight);
        Assert.Equal(ReplayMediaPolicy.HighSkillScore, highSkill.Score.PriorityWeight);
        Assert.Equal(ReplayMediaPolicy.OrdinaryScore, ordinary.Score.PriorityWeight);
        Assert.Equal(0, requested.Score.Recency);
        Assert.Equal(ReplayMediaPolicy.ScoreComponentCap, notable.Score.NotableStrength);
        Assert.True(requested.Score.Total > notable.Score.Total);
        Assert.True(notable.Score.Total > highSkill.Score.Total);
        Assert.True(highSkill.Score.Total > ordinary.Score.Total);
        Assert.Equal(ReplayMediaReason.EligibleRequested, requested.PublicationReason);
        Assert.Equal(ReplayMediaReason.EligibleCurated, notable.PublicationReason);
        Assert.Equal(ReplayMediaReason.EligibleCurated, highSkill.PublicationReason);
        Assert.Equal(ReplayMediaReason.EligibleCurated, ordinary.PublicationReason);
    }

    [Fact]
    public void PaidRequest_DoesNotBypassAlreadyPublished()
    {
        ReplayMediaDecision decision = Decide(
            Sample(
                recordAndUpload: true,
                alreadyPublished: true,
                events: new[] { Pentakill("Li-Ming", 10) }
            )
        );

        Assert.False(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.AlreadyPublished, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.AlreadyPublished, decision.PublicationReason);
        Assert.Equal(ReplayMediaPriority.Requested, decision.Priority);
        Assert.True(decision.Score.PriorityWeight > ReplayMediaPolicy.NotableScore);
        Assert.Single(decision.NotableEvents);
    }

    [Fact]
    public void PaidRequest_DoesNotBypassAlreadyScheduled()
    {
        ReplayMediaDecision decision = Decide(
            Sample(recordAndUpload: true, alreadyScheduled: true)
        );

        Assert.False(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.AlreadyScheduled, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.AlreadyScheduled, decision.PublicationReason);
        Assert.Equal(ReplayMediaPriority.Requested, decision.Priority);
    }

    [Fact]
    public void PaidRequest_DoesNotBypassInOutbox()
    {
        ReplayMediaDecision decision = Decide(Sample(recordAndUpload: true, inOutbox: true));

        Assert.False(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.InOutbox, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.InOutbox, decision.PublicationReason);
        Assert.Equal(ReplayMediaPriority.Requested, decision.Priority);
    }

    [Fact]
    public void PaidRequest_DoesNotBypassAwaitingCompletion()
    {
        ReplayMediaDecision decision = Decide(
            Sample(recordAndUpload: true, includeCompletion: false)
        );

        Assert.True(decision.Record);
        Assert.Equal(ReplayMediaReason.RecordedRequested, decision.RecordingReason);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.AwaitingCompletion, decision.PublicationReason);
        Assert.Equal(ReplayMediaPriority.Requested, decision.Priority);
    }

    [Fact]
    public void PaidRequest_DoesNotBypassIncomplete()
    {
        ReplayMediaDecision decision = Decide(Sample(recordAndUpload: true, isComplete: false));

        Assert.True(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.Incomplete, decision.PublicationReason);
        Assert.Equal(ReplayMediaPriority.Requested, decision.Priority);
    }

    [Fact]
    public void PaidRequest_DoesNotBypassAwaitingMedia()
    {
        ReplayMediaDecision decision = Decide(Sample(recordAndUpload: true, includeMedia: false));

        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.AwaitingMedia, decision.PublicationReason);
        Assert.Equal(ReplayMediaPriority.Requested, decision.Priority);
    }

    [Fact]
    public void PaidRequest_DoesNotBypassUnfinalizedMedia()
    {
        ReplayMediaDecision decision = Decide(
            Sample(recordAndUpload: true, isFinalized: false, isCorrelated: true)
        );

        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.MediaNotFinalized, decision.PublicationReason);
    }

    [Fact]
    public void PaidRequest_DoesNotBypassUncorrelatedMedia()
    {
        ReplayMediaDecision decision = Decide(
            Sample(recordAndUpload: true, isFinalized: true, isCorrelated: false)
        );

        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.MediaNotCorrelated, decision.PublicationReason);
    }

    [Fact]
    public void RecordAndUploadWithoutViewerFlag_IsStillRequested()
    {
        ReplayMediaDecision decision = Decide(
            Sample(recordAndUpload: true, viewerRequested: false)
        );

        Assert.Equal(ReplayMediaPriority.Requested, decision.Priority);
        Assert.Equal(ReplayMediaReason.RecordedRequested, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.EligibleRequested, decision.PublicationReason);
    }

    [Fact]
    public void SpectateOnly_IsNotARecordingOrPublicationCandidate()
    {
        ReplayMediaDecision decision = Decide(
            Sample(
                viewerRequested: true,
                recordAndUpload: false,
                rank: "Grandmaster",
                mmr: 3500,
                events: new[] { Pentakill("Li-Ming", 20) }
            ),
            Settings(
                recording: ReplayRecordingMode.All,
                publication: ReplayPublicationMode.AllEligible
            )
        );

        Assert.False(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.SpectateOnly, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.SpectateOnly, decision.PublicationReason);
        Assert.Equal(ReplayMediaPriority.Notable, decision.Priority);
        Assert.NotEqual(ReplayMediaPriority.Requested, decision.Priority);
    }

    [Fact]
    public void MissingOptionalFacts_AreOrdinary()
    {
        ReplayMediaDecision decision = Decide(
            Sample(rank: null, mmr: null, focusHero: null, events: null, roster: null)
        );

        Assert.Equal(ReplayMediaPriority.Ordinary, decision.Priority);
        Assert.Equal(0, decision.Score.Skill);
        Assert.Equal(0, decision.Score.NotableStrength);
        Assert.Empty(decision.NotableEvents);
        Assert.Null(decision.FocusHero);
        Assert.Null(decision.Rank);
        Assert.Null(decision.AverageMmr);
        Assert.Equal(ReplayMediaReason.RecordedOrdinary, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.EligibleCurated, decision.PublicationReason);
    }

    [Fact]
    public void NoHighSkillThreshold_TreatsEveryRankAsOrdinary()
    {
        ReplayMediaPolicySettings settings = Settings();
        settings.MinimumHighSkillRank = null;
        settings.MinimumHighSkillMmr = null;

        ReplayMediaDecision decision = Decide(Sample(rank: "Grandmaster", mmr: 4000), settings);

        Assert.Equal(ReplayMediaPriority.Ordinary, decision.Priority);
        Assert.Equal(0, decision.Score.Skill);
    }

    [Fact]
    public void HighSkill_RankAtThreshold_QualifiesWithNoSkillPoints()
    {
        ReplayMediaDecision decision = Decide(Sample(rank: "Master", mmr: 1000));

        Assert.Equal(ReplayMediaPriority.HighSkill, decision.Priority);
        Assert.Equal(0, decision.Score.Skill);
        Assert.Equal(ReplayMediaReason.RecordedHighSkill, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.EligibleCurated, decision.PublicationReason);
    }

    [Fact]
    public void HighSkill_Grandmaster_AddsRankSteps()
    {
        ReplayMediaDecision decision = Decide(Sample(rank: "Grandmaster", mmr: null));

        Assert.Equal(ReplayMediaPriority.HighSkill, decision.Priority);
        Assert.Equal(100, decision.Score.Skill);
    }

    [Fact]
    public void HighSkill_MmrAtThreshold_QualifiesWhenRankIsLower()
    {
        ReplayMediaDecision decision = Decide(Sample(rank: "Gold", mmr: 2800));

        Assert.Equal(ReplayMediaPriority.HighSkill, decision.Priority);
        Assert.Equal(0, decision.Score.Skill);
    }

    [Fact]
    public void HighSkill_MmrAboveThreshold_AddsTheDelta()
    {
        ReplayMediaDecision decision = Decide(Sample(rank: "Gold", mmr: 2900));

        Assert.Equal(ReplayMediaPriority.HighSkill, decision.Priority);
        Assert.Equal(100, decision.Score.Skill);
    }

    [Fact]
    public void MmrOneBelowThreshold_AndLowerRank_IsOrdinary()
    {
        ReplayMediaDecision decision = Decide(Sample(rank: "Diamond 3", mmr: 2799));

        Assert.Equal(ReplayMediaPriority.Ordinary, decision.Priority);
        Assert.Equal(0, decision.Score.Skill);
    }

    [Fact]
    public void TeamKillClips_AreNotableEvidence()
    {
        IReadOnlyList<TeamKillClip> clips = TeamKillClips.Select(
            new[]
            {
                Death(100, "Li-Ming", "Artanis"),
                Death(103, "Li-Ming", "Butcher"),
                Death(106, "Li-Ming", "Chromie"),
                Death(109, "Li-Ming", "Diablo"),
                Death(112, "Li-Ming", "E.T.C."),
            }
        );

        ReplayMediaDecision decision = Decide(Sample(events: clips, rank: "Bronze", mmr: 1000));

        Assert.Equal(ReplayMediaPriority.Notable, decision.Priority);
        Assert.Equal(2, decision.NotableEvents.Count);
        Assert.Equal(TeamKillClips.PentakillKind, decision.NotableEvents[0].Kind);
        Assert.Equal("Li-Ming", decision.NotableEvents[0].Hero);
        Assert.Equal(TeamKillClips.TeamWipeKind, decision.NotableEvents[1].Kind);
        Assert.Equal(
            ReplayMediaPolicy.PentakillScore + ReplayMediaPolicy.TeamWipeScore,
            decision.Score.NotableStrength
        );
        Assert.Equal(ReplayMediaReason.RecordedNotable, decision.RecordingReason);
    }

    [Fact]
    public void UnknownEventKinds_AreIgnored()
    {
        ReplayMediaDecision decision = Decide(
            Sample(
                events: new List<TeamKillClip>
                {
                    Wipe("Zeratul", 50),
                    new TeamKillClip("Pentakill", "Nova", 1, 2, 1, 2, "pentakill"),
                    new TeamKillClip("highlight", "Nova", 0, 1, 0, 1, "pentakill"),
                    Pentakill("Li-Ming", 10),
                }
            )
        );

        Assert.Equal(2, decision.NotableEvents.Count);
        Assert.Equal(TeamKillClips.PentakillKind, decision.NotableEvents[0].Kind);
        Assert.Equal("Li-Ming", decision.NotableEvents[0].Hero);
        Assert.Equal(10, decision.NotableEvents[0].HudStartSecond);
        Assert.Equal(TeamKillClips.TeamWipeKind, decision.NotableEvents[1].Kind);
        Assert.Equal(ReplayMediaPriority.Notable, decision.Priority);
    }

    [Fact]
    public void DecisionEvidence_DoesNotFollowLaterListChanges()
    {
        var events = new List<TeamKillClip> { Pentakill("Li-Ming", 10) };
        ReplayMediaDecision decision = Decide(Sample(events: events));
        events.Clear();
        events.Add(Wipe("Nova", 1));

        TeamKillClip kept = Assert.Single(decision.NotableEvents);
        Assert.Equal("Li-Ming", kept.Hero);
        Assert.Equal(TeamKillClips.PentakillKind, kept.Kind);
    }

    [Fact]
    public void NotableStrength_IsCappedBelowTheNextPriority()
    {
        var clips = new List<TeamKillClip>();
        for (int second = 0; second < 500; second++)
        {
            clips.Add(Pentakill("X", second));
        }

        ReplayMediaDecision decision = Decide(Sample(events: clips, rank: "Bronze", mmr: 1000));

        Assert.Equal(ReplayMediaPriority.Notable, decision.Priority);
        Assert.Equal(ReplayMediaPolicy.ScoreComponentCap, decision.Score.NotableStrength);
        Assert.True(decision.Score.Total < ReplayMediaPolicy.RequestedScore);
    }

    [Fact]
    public void ReplayId_IsATieBreakAndNotPartOfTheTotal()
    {
        ReplayMediaDecision low = Decide(Sample(replayId: 10));
        ReplayMediaDecision high = Decide(Sample(replayId: 20));

        Assert.Equal(low.Score.Total, high.Score.Total);
        Assert.Equal(10, low.Score.TieBreakReplayId);
        Assert.Equal(20, high.Score.TieBreakReplayId);
        Assert.Equal(GameDate.Ticks, low.Score.TieBreakGameDateTicks);
    }

    [Fact]
    public void RequestorAndRoster_DoNotChangeTheDecision()
    {
        ReplayMediaDecision named = Decide(
            Sample(
                requestedBy: "ViewerA",
                roster: new[]
                {
                    new ReplayMediaPlayer
                    {
                        Team = 0,
                        Hero = "Li-Ming",
                        Name = "ViewerA",
                        BattleTag = 1,
                    },
                }
            )
        );
        ReplayMediaDecision unnamed = Decide(Sample(requestedBy: "ViewerB"));

        Assert.Equal(named.Score, unnamed.Score);
        Assert.Equal(named.Priority, unnamed.Priority);
        Assert.Equal(named.RecordingReason, unnamed.RecordingReason);
        Assert.Equal(named.PublicationReason, unnamed.PublicationReason);
    }

    [Fact]
    public void MissingGameDate_FailsClosed_IncludingPaidRequests()
    {
        ReplayMediaDecision selected = Decide(
            Sample(includeGameDate: false, recordAndUpload: true)
        );
        ReplayMediaDecision all = Decide(
            Sample(includeGameDate: false),
            Settings(recording: ReplayRecordingMode.All)
        );

        Assert.False(selected.Record);
        Assert.False(selected.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.MissingGameDate, selected.RecordingReason);
        Assert.Equal(ReplayMediaReason.MissingGameDate, selected.PublicationReason);
        Assert.True(all.Record);
        Assert.Equal(ReplayMediaReason.RecordedAll, all.RecordingReason);
        Assert.False(all.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.MissingGameDate, all.PublicationReason);
    }

    [Fact]
    public void NullInput_FailsClosed()
    {
        ReplayMediaDecision decision = ReplayMediaPolicy.Evaluate(null, Settings(), Now);

        Assert.False(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.MissingInput, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.MissingInput, decision.PublicationReason);
        Assert.Empty(decision.NotableEvents);
        Assert.Empty(decision.ConfigurationErrors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NonPositiveReplayId_FailsClosed(int replayId)
    {
        ReplayMediaDecision decision = Decide(Sample(replayId: replayId));

        Assert.False(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.MissingIdentity, decision.RecordingReason);
        Assert.Null(decision.ReplayId);
    }

    [Fact]
    public void MissingReplayId_FailsClosed()
    {
        ReplayMediaDecision decision = Decide(Sample(replayId: null));

        Assert.Equal(ReplayMediaReason.MissingIdentity, decision.PublicationReason);
        Assert.False(decision.PublicationCandidate);
        Assert.Null(decision.ReplayId);
    }

    [Fact]
    public void NegativeMmr_FailsClosed()
    {
        ReplayMediaDecision decision = Decide(Sample(mmr: -0.1));

        Assert.False(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.MalformedInput, decision.RecordingReason);
        Assert.Null(decision.AverageMmr);
    }

    [Fact]
    public void NonFiniteMmr_FailsClosed()
    {
        ReplayMediaDecision nan = Decide(Sample(mmr: double.NaN));
        ReplayMediaDecision infinity = Decide(Sample(mmr: double.PositiveInfinity));

        Assert.Equal(ReplayMediaReason.MalformedInput, nan.PublicationReason);
        Assert.Equal(ReplayMediaReason.MalformedInput, infinity.PublicationReason);
        Assert.False(nan.PublicationCandidate);
        Assert.False(infinity.Record);
        Assert.Null(nan.AverageMmr);
    }

    [Fact]
    public void GameDateNearMaxValue_FailsClosed()
    {
        ReplayMediaDecision decision = Decide(
            Sample(gameDate: DateTime.SpecifyKind(DateTime.MaxValue.AddDays(-1), DateTimeKind.Utc))
        );

        Assert.False(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.MalformedInput, decision.PublicationReason);
    }

    [Fact]
    public void LocalClock_FailsClosed()
    {
        DateTime local = new DateTime(2026, 9, 29, 18, 0, 0, DateTimeKind.Local);
        ReplayMediaDecision decision = ReplayMediaPolicy.Evaluate(Sample(), Settings(), local);

        Assert.False(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.ClockNotUtc, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.ClockNotUtc, decision.PublicationReason);
        Assert.Equal(DateTimeKind.Local, decision.EvaluatedAtUtc.Kind);
    }

    [Fact]
    public void LocalGameDate_FailsClosed()
    {
        ReplayMediaDecision decision = Decide(
            Sample(gameDate: new DateTime(2026, 9, 28, 18, 0, 0, DateTimeKind.Local))
        );

        Assert.Equal(ReplayMediaReason.GameDateNotUtc, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.GameDateNotUtc, decision.PublicationReason);
        Assert.False(decision.PublicationCandidate);
        Assert.Null(decision.GameDateUtc);
    }

    [Fact]
    public void NullSettings_FailsClosed()
    {
        ReplayMediaDecision decision = ReplayMediaPolicy.Evaluate(Sample(), null, Now);

        Assert.False(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.ConfigurationInvalid, decision.RecordingReason);
        Assert.Contains(
            ReplayMediaConfigurationError.SettingsMissing,
            decision.ConfigurationErrors
        );
        Assert.Null(decision.ConfigurationVersion);
        Assert.Equal(ReplayMediaPolicy.PolicyVersion, decision.PolicyVersion);
    }

    [Fact]
    public void InvalidRecordingMode_FailsClosed()
    {
        ReplayMediaPolicySettings settings = Settings(recording: ReplayRecordingMode.All);
        settings.RecordingMode = (ReplayRecordingMode)99;

        ReplayMediaDecision decision = Decide(settings: settings);

        Assert.False(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.ConfigurationInvalid, decision.PublicationReason);
        Assert.Contains(
            ReplayMediaConfigurationError.RecordingModeInvalid,
            decision.ConfigurationErrors
        );
        Assert.NotEqual(ReplayMediaReason.RecordedAll, decision.RecordingReason);
    }

    [Fact]
    public void InvalidPublicationMode_DoesNotPublishAll()
    {
        ReplayMediaPolicySettings settings = Settings(
            recording: ReplayRecordingMode.All,
            publication: ReplayPublicationMode.AllEligible
        );
        settings.PublicationMode = (ReplayPublicationMode)99;

        ReplayMediaDecision decision = Decide(settings: settings);

        Assert.False(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.ConfigurationInvalid, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.ConfigurationInvalid, decision.PublicationReason);
        Assert.Contains(
            ReplayMediaConfigurationError.PublicationModeInvalid,
            decision.ConfigurationErrors
        );
        Assert.NotEqual(ReplayMediaReason.EligibleAll, decision.PublicationReason);
    }

    [Theory]
    [InlineData("ordinary", "ordinary-max-age-negative")]
    [InlineData("high-skill", "high-skill-max-age-negative")]
    [InlineData("notable", "notable-max-age-negative")]
    [InlineData("requested", "requested-max-age-negative")]
    public void NegativeDuration_FailsClosed(string which, string error)
    {
        ReplayMediaPolicySettings settings = Settings();
        TimeSpan negative = TimeSpan.FromMinutes(-1);
        switch (which)
        {
            case "ordinary":
                settings.OrdinaryCandidateMaxAge = negative;
                break;
            case "high-skill":
                settings.HighSkillCandidateMaxAge = negative;
                break;
            case "notable":
                settings.NotableCandidateMaxAge = negative;
                break;
            default:
                settings.RequestedCandidateMaxAge = negative;
                break;
        }

        AssertInvalid(settings, error);
    }

    [Theory]
    [InlineData("ordinary", "ordinary-max-age-too-large")]
    [InlineData("high-skill", "high-skill-max-age-too-large")]
    [InlineData("notable", "notable-max-age-too-large")]
    [InlineData("requested", "requested-max-age-too-large")]
    public void HugeDuration_FailsClosed(string which, string error)
    {
        ReplayMediaPolicySettings settings = Settings();
        TimeSpan huge = TimeSpan.FromDays(ReplayMediaPolicy.MaxCandidateAgeDays + 1);
        switch (which)
        {
            case "ordinary":
                settings.OrdinaryCandidateMaxAge = huge;
                break;
            case "high-skill":
                settings.HighSkillCandidateMaxAge = huge;
                break;
            case "notable":
                settings.NotableCandidateMaxAge = huge;
                break;
            default:
                settings.RequestedCandidateMaxAge = huge;
                break;
        }

        AssertInvalid(settings, error);
    }

    [Fact]
    public void BlankConfigurationVersion_FailsClosed()
    {
        ReplayMediaPolicySettings settings = Settings();
        settings.Version = "  ";

        AssertInvalid(settings, ReplayMediaConfigurationError.ConfigurationVersionMissing);
    }

    [Fact]
    public void RequireCurrentPatch_WithoutVersion_FailsClosed()
    {
        ReplayMediaPolicySettings settings = Settings();
        settings.MinimumGameVersion = null;

        ReplayMediaDecision decision = AssertInvalid(
            settings,
            ReplayMediaConfigurationError.MinimumGameVersionMissing
        );
        Assert.NotEqual(ReplayMediaReason.PublicationDisabled, decision.PublicationReason);
    }

    [Fact]
    public void NonNumericPatchFloor_FailsClosed()
    {
        ReplayMediaPolicySettings settings = Settings();
        settings.MinimumGameVersion = "2.57.beta";

        AssertInvalid(settings, ReplayMediaConfigurationError.MinimumGameVersionInvalid);
    }

    [Fact]
    public void UnknownHighSkillRank_FailsClosed()
    {
        ReplayMediaPolicySettings settings = Settings();
        settings.MinimumHighSkillRank = "Challenger";

        AssertInvalid(settings, ReplayMediaConfigurationError.HighSkillRankInvalid);
    }

    [Fact]
    public void NegativeHighSkillMmr_FailsClosed()
    {
        ReplayMediaPolicySettings settings = Settings();
        settings.MinimumHighSkillMmr = -1;

        AssertInvalid(settings, ReplayMediaConfigurationError.HighSkillMmrNegative);
    }

    [Fact]
    public void RecordingDisabledWhilePublishing_FailsClosed()
    {
        ReplayMediaPolicySettings settings = new ReplayMediaPolicySettings
        {
            Version = "1",
            RecordingMode = ReplayRecordingMode.Disabled,
            PublicationMode = ReplayPublicationMode.Curated,
            RequireCurrentPatch = false,
        };

        AssertInvalid(settings, ReplayMediaConfigurationError.RecordingDisabledWhilePublishing);
    }

    [Fact]
    public void InvalidConfiguration_CollectsEveryProblemAndDoesNotPublish()
    {
        var settings = new ReplayMediaPolicySettings
        {
            Version = " ",
            RecordingMode = ReplayRecordingMode.Disabled,
            PublicationMode = ReplayPublicationMode.Curated,
            RequireCurrentPatch = true,
            MinimumGameVersion = null,
            OrdinaryCandidateMaxAge = TimeSpan.FromTicks(-1),
            MinimumHighSkillRank = "Nope",
            MinimumHighSkillMmr = -5,
        };

        ReplayMediaDecision decision = ReplayMediaPolicy.Evaluate(Sample(), settings, Now);

        Assert.False(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.ConfigurationInvalid, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.ConfigurationInvalid, decision.PublicationReason);
        Assert.Equal(ReplayMediaPolicy.Validate(settings), decision.ConfigurationErrors);
        Assert.Contains(
            ReplayMediaConfigurationError.ConfigurationVersionMissing,
            decision.ConfigurationErrors
        );
        Assert.Contains(
            ReplayMediaConfigurationError.RecordingDisabledWhilePublishing,
            decision.ConfigurationErrors
        );
        Assert.Contains(
            ReplayMediaConfigurationError.MinimumGameVersionMissing,
            decision.ConfigurationErrors
        );
        Assert.Contains(
            ReplayMediaConfigurationError.OrdinaryMaxAgeNegative,
            decision.ConfigurationErrors
        );
        Assert.Contains(
            ReplayMediaConfigurationError.HighSkillRankInvalid,
            decision.ConfigurationErrors
        );
        Assert.Contains(
            ReplayMediaConfigurationError.HighSkillMmrNegative,
            decision.ConfigurationErrors
        );
    }

    private static ReplayMediaDecision AssertInvalid(
        ReplayMediaPolicySettings settings,
        string error
    )
    {
        ReplayMediaDecision decision = Decide(Sample(), settings);
        Assert.False(decision.Record);
        Assert.False(decision.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.ConfigurationInvalid, decision.RecordingReason);
        Assert.Equal(ReplayMediaReason.ConfigurationInvalid, decision.PublicationReason);
        Assert.Contains(error, decision.ConfigurationErrors);
        return decision;
    }

    private static void AssertSame(ReplayMediaDecision left, ReplayMediaDecision right)
    {
        Assert.Equal(left.PolicyVersion, right.PolicyVersion);
        Assert.Equal(left.ConfigurationVersion, right.ConfigurationVersion);
        Assert.Equal(left.EvaluatedAtUtc, right.EvaluatedAtUtc);
        Assert.Equal(left.Record, right.Record);
        Assert.Equal(left.RecordingReason, right.RecordingReason);
        Assert.Equal(left.PublicationCandidate, right.PublicationCandidate);
        Assert.Equal(left.PublicationReason, right.PublicationReason);
        Assert.Equal(left.Priority, right.Priority);
        Assert.Equal(left.Score, right.Score);
        Assert.Equal(left.NotableEvents, right.NotableEvents);
        Assert.Equal(left.CandidateExpiresAtUtc, right.CandidateExpiresAtUtc);
        Assert.Equal(left.RecordingMode, right.RecordingMode);
        Assert.Equal(left.PublicationMode, right.PublicationMode);
        Assert.Equal(left.SchedulerCurationRequired, right.SchedulerCurationRequired);
        Assert.Equal(left.ConfigurationErrors, right.ConfigurationErrors);
        Assert.Equal(left.ReplayId, right.ReplayId);
        Assert.Equal(left.GameDateUtc, right.GameDateUtc);
        Assert.Equal(left.GameVersion, right.GameVersion);
        Assert.Equal(left.Map, right.Map);
        Assert.Equal(left.GameMode, right.GameMode);
        Assert.Equal(left.Rank, right.Rank);
        Assert.Equal(left.AverageMmr, right.AverageMmr);
        Assert.Equal(left.FocusHero, right.FocusHero);
    }

    private static ReplayMediaDecision Decide(
        ReplayMediaPolicyInput input = null,
        ReplayMediaPolicySettings settings = null,
        DateTime? now = null
    )
    {
        return ReplayMediaPolicy.Evaluate(input ?? Sample(), settings ?? Settings(), now ?? Now);
    }

    private static ReplayMediaPolicySettings Settings(
        ReplayRecordingMode recording = ReplayRecordingMode.Selected,
        ReplayPublicationMode publication = ReplayPublicationMode.Curated
    )
    {
        return new ReplayMediaPolicySettings
        {
            Version = "1",
            RecordingMode = recording,
            PublicationMode = publication,
            RequireCurrentPatch = true,
            MinimumGameVersion = "2.57.0.98285",
            RequestsBypassPatchRequirement = true,
            MinimumHighSkillRank = "Master",
            MinimumHighSkillMmr = 2800,
            OrdinaryCandidateMaxAge = TimeSpan.FromDays(3),
            HighSkillCandidateMaxAge = TimeSpan.FromDays(7),
            NotableCandidateMaxAge = TimeSpan.FromDays(7),
            RequestedCandidateMaxAge = TimeSpan.FromDays(14),
        };
    }

    private static ReplayMediaPolicyInput Sample(
        int? replayId = 65389750,
        string version = "2.57.0.98285",
        string rank = "Diamond 3",
        double? mmr = 2500,
        string focusHero = "Li-Ming",
        bool recordAndUpload = false,
        bool viewerRequested = false,
        string requestedBy = null,
        IReadOnlyList<TeamKillClip> events = null,
        IReadOnlyList<ReplayMediaPlayer> roster = null,
        bool includeGameDate = true,
        DateTime? gameDate = null,
        bool includeCompletion = true,
        bool isComplete = true,
        bool includeMedia = true,
        bool isFinalized = true,
        bool isCorrelated = true,
        bool alreadyPublished = false,
        bool alreadyScheduled = false,
        bool inOutbox = false,
        string map = "Dragon Shire",
        string mode = "Storm League"
    )
    {
        return new ReplayMediaPolicyInput
        {
            ReplayId = replayId,
            GameDateUtc = includeGameDate ? gameDate ?? GameDate : null,
            GameVersion = version,
            Map = map,
            GameMode = mode,
            Rank = rank,
            AverageMmr = mmr,
            FocusHero = focusHero,
            ViewerRequested = viewerRequested,
            RecordAndUpload = recordAndUpload,
            RequestedBy = requestedBy,
            NotableEvents = events,
            Roster = roster,
            AlreadyPublished = alreadyPublished,
            AlreadyScheduled = alreadyScheduled,
            InOutbox = inOutbox,
            Completion = includeCompletion
                ? new ReplayMediaCompletion { IsVerifiedComplete = isComplete }
                : null,
            Media = includeMedia
                ? new ReplayMediaFinalization
                {
                    IsFinalized = isFinalized,
                    IsCorrelated = isCorrelated,
                }
                : null,
        };
    }

    private static TeamKillClip Pentakill(string hero, int second)
    {
        return new TeamKillClip(
            TeamKillClips.PentakillKind,
            hero,
            second,
            second + 4,
            second,
            second + 8,
            hero + " pentakill"
        );
    }

    private static TeamKillClip Wipe(string hero, int second)
    {
        return new TeamKillClip(
            TeamKillClips.TeamWipeKind,
            hero,
            second,
            second + 4,
            second,
            second + 8,
            "team wipe"
        );
    }

    private static TeamKillDeath Death(int second, string killer, string victim)
    {
        return new TeamKillDeath(second, killer, victim);
    }
}
