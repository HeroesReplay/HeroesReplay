using System;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Spectating.Session;
using Xunit;

namespace HeroesReplay.Tests.Unit.MediaPolicy;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayMediaPublicationTests
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

    [Theory]
    [InlineData(MatchOutcome.None)]
    [InlineData(MatchOutcome.ClientCrashed)]
    [InlineData(MatchOutcome.ClientHung)]
    [InlineData(MatchOutcome.VersionMismatch)]
    [InlineData(MatchOutcome.RegionUnavailable)]
    [InlineData(MatchOutcome.BuildNotInstalled)]
    [InlineData(MatchOutcome.LoadTimedOut)]
    [InlineData(MatchOutcome.Stopped)]
    [InlineData(MatchOutcome.Canceled)]
    public void UnverifiedOutcome_IsNotEligible(MatchOutcome outcome)
    {
        ReplayMediaDecision recorded = Awaiting();

        ReplayMediaDecision result = MediaPolicyPublication.Apply(
            recorded,
            Verified(ObsRecordingResult.FinalizedAt(@"C:\temp\match.mp4"), outcome)
        );

        Assert.False(result.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.Incomplete, result.PublicationReason);
        AssertRecordingKept(recorded, result);
    }

    [Fact]
    public void MissingMatchClock_IsNotEligible()
    {
        ReplayMediaDecision result = MediaPolicyPublication.Apply(
            Awaiting(),
            Verified(
                ObsRecordingResult.FinalizedAt(@"C:\temp\match.mp4"),
                clock: false,
                samples: 4,
                recordedFor: TimeSpan.FromMinutes(3)
            )
        );

        Assert.False(result.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.Incomplete, result.PublicationReason);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(2, 1)]
    public void ShortOrUnsampledRecording_IsNotEligible(int samples, int minutes)
    {
        ReplayMediaDecision result = MediaPolicyPublication.Apply(
            Awaiting(),
            Verified(
                ObsRecordingResult.FinalizedAt(@"C:\temp\match.mp4"),
                samples: samples,
                recordedFor: TimeSpan.FromMinutes(minutes)
            )
        );

        Assert.False(result.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.Incomplete, result.PublicationReason);
    }

    [Fact]
    public void AbsentOrUnfinalizedMedia_IsNotEligible()
    {
        ReplayMediaDecision recorded = Awaiting();

        ReplayMediaDecision missing = MediaPolicyPublication.Apply(recorded, Verified(null));
        ReplayMediaDecision started = MediaPolicyPublication.Apply(
            recorded,
            Verified(ObsRecordingResult.Started())
        );
        ReplayMediaDecision failed = MediaPolicyPublication.Apply(
            recorded,
            Verified(ObsRecordingResult.Failed(ObsOutputFailure.Timeout, "stopped"))
        );

        Assert.Equal(ReplayMediaReason.MediaNotFinalized, missing.PublicationReason);
        Assert.Equal(ReplayMediaReason.MediaNotFinalized, started.PublicationReason);
        Assert.Equal(ReplayMediaReason.MediaNotFinalized, failed.PublicationReason);
        Assert.False(missing.PublicationCandidate);
        Assert.False(started.PublicationCandidate);
        Assert.False(failed.PublicationCandidate);
        AssertRecordingKept(recorded, started);
    }

    [Fact]
    public void FinalizedOwnedPath_IsEligible()
    {
        ReplayMediaDecision recorded = Awaiting();

        ReplayMediaDecision result = MediaPolicyPublication.Apply(
            recorded,
            Verified(ObsRecordingResult.FinalizedAt(@"C:\temp\match.mp4"))
        );

        Assert.True(result.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.EligibleCurated, result.PublicationReason);
        Assert.True(result.SchedulerCurationRequired);
        AssertRecordingKept(recorded, result);
    }

    [Fact]
    public void AwaitingMedia_CanBecomeEligible()
    {
        ReplayMediaDecision recorded = Awaiting();
        recorded = new ReplayMediaDecision
        {
            PolicyVersion = recorded.PolicyVersion,
            EvaluatedAtUtc = recorded.EvaluatedAtUtc,
            Record = recorded.Record,
            RecordingReason = recorded.RecordingReason,
            PublicationReason = ReplayMediaReason.AwaitingMedia,
            Priority = recorded.Priority,
            Score = recorded.Score,
            NotableEvents = recorded.NotableEvents,
            CandidateExpiresAtUtc = recorded.CandidateExpiresAtUtc,
            RecordingMode = recorded.RecordingMode,
            PublicationMode = recorded.PublicationMode,
            ReplayId = recorded.ReplayId,
            GameDateUtc = recorded.GameDateUtc,
        };

        ReplayMediaDecision result = MediaPolicyPublication.Apply(
            recorded,
            Verified(ObsRecordingResult.FinalizedAt(@"C:\temp\match.mp4"))
        );

        Assert.True(result.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.EligibleCurated, result.PublicationReason);
    }

    [Fact]
    public void DuplicateState_PrefersPublishedOverTheOutbox()
    {
        MediaPublicationFacts facts = Verified(
            ObsRecordingResult.FinalizedAt(@"C:\temp\match.mp4")
        );
        facts = new MediaPublicationFacts
        {
            Outcome = facts.Outcome,
            MatchClockSeen = facts.MatchClockSeen,
            HudSamples = facts.HudSamples,
            RecordedFor = facts.RecordedFor,
            Recording = facts.Recording,
            AlreadyPublished = true,
            AlreadyScheduled = true,
            InOutbox = true,
        };

        ReplayMediaDecision result = MediaPolicyPublication.Apply(Awaiting(), facts);

        Assert.False(result.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.AlreadyPublished, result.PublicationReason);
    }

    [Fact]
    public void AlreadyScheduled_IsNotEligible()
    {
        MediaPublicationFacts verified = Verified(
            ObsRecordingResult.FinalizedAt(@"C:\temp\match.mp4")
        );
        ReplayMediaDecision result = MediaPolicyPublication.Apply(
            Awaiting(),
            new MediaPublicationFacts
            {
                Outcome = verified.Outcome,
                MatchClockSeen = true,
                HudSamples = verified.HudSamples,
                RecordedFor = verified.RecordedFor,
                Recording = verified.Recording,
                AlreadyScheduled = true,
            }
        );

        Assert.False(result.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.AlreadyScheduled, result.PublicationReason);
    }

    [Theory]
    [InlineData(ReplayMediaReason.NotRequested)]
    [InlineData(ReplayMediaReason.PublicationDisabled)]
    [InlineData(ReplayMediaReason.SpectateOnly)]
    [InlineData(ReplayMediaReason.ConfigurationInvalid)]
    [InlineData(ReplayMediaReason.EligibleCurated)]
    public void ClosedPublicationReason_StaysClosed(string reason)
    {
        ReplayMediaDecision recorded = Awaiting(publicationReason: reason);

        ReplayMediaDecision result = MediaPolicyPublication.Apply(
            recorded,
            Verified(
                ObsRecordingResult.FinalizedAt(@"C:\temp\match.mp4"),
                outcome: MatchOutcome.Canceled
            )
        );

        Assert.Equal(reason, result.PublicationReason);
        Assert.Equal(recorded.PublicationCandidate, result.PublicationCandidate);
        Assert.Equal(recorded.Record, result.Record);
    }

    [Fact]
    public void EligibleReason_FollowsStoredPriorityAndMode()
    {
        ObsRecordingResult recording = ObsRecordingResult.FinalizedAt(@"C:\temp\match.mp4");

        ReplayMediaDecision requested = MediaPolicyPublication.Apply(
            Awaiting(priority: ReplayMediaPriority.Requested, mode: ReplayPublicationMode.Curated),
            Verified(recording)
        );
        ReplayMediaDecision curated = MediaPolicyPublication.Apply(
            Awaiting(mode: ReplayPublicationMode.Curated),
            Verified(recording)
        );
        ReplayMediaDecision all = MediaPolicyPublication.Apply(
            Awaiting(mode: ReplayPublicationMode.AllEligible),
            Verified(recording)
        );

        Assert.Equal(ReplayMediaReason.EligibleRequested, requested.PublicationReason);
        Assert.True(requested.SchedulerCurationRequired);
        Assert.Equal(ReplayMediaReason.EligibleCurated, curated.PublicationReason);
        Assert.True(curated.SchedulerCurationRequired);
        Assert.Equal(ReplayMediaReason.EligibleAll, all.PublicationReason);
        Assert.False(all.SchedulerCurationRequired);
    }

    [Fact]
    public void StoredVersionAndScore_StayOnThePromotedDecision()
    {
        ReplayMediaDecision recorded = Awaiting(policyVersion: "0");

        ReplayMediaDecision result = MediaPolicyPublication.Apply(
            recorded,
            Verified(ObsRecordingResult.FinalizedAt(@"C:\temp\match.mp4"))
        );

        Assert.Equal("0", result.PolicyVersion);
        Assert.Equal(recorded.Score, result.Score);
        Assert.Equal(recorded.CandidateExpiresAtUtc, result.CandidateExpiresAtUtc);
        Assert.Equal(ReplayRecordingMode.All, result.RecordingMode);
        Assert.Equal(ReplayPublicationMode.Curated, result.PublicationMode);
        Assert.Equal(recorded.EvaluatedAtUtc, result.EvaluatedAtUtc);
    }

    [Fact]
    public void NullDecision_IsDenied()
    {
        ReplayMediaDecision result = MediaPolicyPublication.Apply(null, Verified(null));

        Assert.False(result.Record);
        Assert.False(result.PublicationCandidate);
        Assert.Equal(ReplayMediaReason.MissingInput, result.PublicationReason);
    }

    [Fact]
    public void NullFacts_LeaveTheRecordedDecision()
    {
        ReplayMediaDecision recorded = Awaiting();

        ReplayMediaDecision result = MediaPolicyPublication.Apply(recorded, null);

        Assert.Equal(ReplayMediaReason.AwaitingCompletion, result.PublicationReason);
        Assert.False(result.PublicationCandidate);
        AssertRecordingKept(recorded, result);
    }

    private static void AssertRecordingKept(
        ReplayMediaDecision recorded,
        ReplayMediaDecision result
    )
    {
        Assert.Equal(recorded.Record, result.Record);
        Assert.Equal(recorded.RecordingReason, result.RecordingReason);
        Assert.Equal(recorded.PolicyVersion, result.PolicyVersion);
        Assert.Equal(recorded.Score, result.Score);
        Assert.Equal(recorded.CandidateExpiresAtUtc, result.CandidateExpiresAtUtc);
        Assert.Equal(recorded.RecordingMode, result.RecordingMode);
        Assert.Equal(recorded.PublicationMode, result.PublicationMode);
        Assert.Equal(recorded.EvaluatedAtUtc, result.EvaluatedAtUtc);
        Assert.Equal(recorded.Priority, result.Priority);
        Assert.Equal(recorded.ReplayId, result.ReplayId);
    }

    private static MediaPublicationFacts Verified(
        ObsRecordingResult recording,
        MatchOutcome outcome = MatchOutcome.VerifiedCompleted,
        bool clock = true,
        int samples = 2,
        TimeSpan? recordedFor = null
    )
    {
        return new MediaPublicationFacts
        {
            Outcome = outcome,
            MatchClockSeen = clock,
            HudSamples = samples,
            RecordedFor = recordedFor ?? TimeSpan.FromMinutes(2),
            Recording = recording,
        };
    }

    private static ReplayMediaDecision Awaiting(
        ReplayPublicationMode mode = ReplayPublicationMode.Curated,
        ReplayMediaPriority priority = ReplayMediaPriority.Ordinary,
        string policyVersion = "1",
        string publicationReason = ReplayMediaReason.AwaitingCompletion
    )
    {
        bool candidate = publicationReason == ReplayMediaReason.EligibleCurated;
        return new ReplayMediaDecision
        {
            PolicyVersion = policyVersion,
            ConfigurationVersion = "1",
            EvaluatedAtUtc = Now,
            Record = true,
            RecordingReason = ReplayMediaReason.RecordedAll,
            PublicationCandidate = candidate,
            PublicationReason = publicationReason,
            Priority = priority,
            Score = new ReplayMediaScore(100000, 10, 0, 0, 100010, 65389750, GameDate.Ticks),
            NotableEvents = Array.Empty<TeamKillClip>(),
            CandidateExpiresAtUtc = GameDate.AddDays(3),
            RecordingMode = ReplayRecordingMode.All,
            PublicationMode = mode,
            SchedulerCurationRequired = candidate && mode == ReplayPublicationMode.Curated,
            ConfigurationErrors = Array.Empty<string>(),
            ReplayId = 65389750,
            GameDateUtc = GameDate,
            GameVersion = "2.55",
            Map = "Dragon Shire",
            GameMode = "Storm League",
            Rank = "Diamond 3",
            AverageMmr = 2500,
            FocusHero = "Li-Ming",
        };
    }
}
