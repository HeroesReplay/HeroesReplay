using System;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Recording;
using HeroesReplay.Core.Spectating.Session;

namespace HeroesReplay.Core.MediaPolicy;

/// <summary>
/// Facts known only after the session stops. They do not redo the pre-launch recording decision.
/// </summary>
public sealed class MediaPublicationFacts
{
    public MatchOutcome Outcome { get; init; }
    public bool MatchClockSeen { get; init; }
    public int HudSamples { get; init; }
    public TimeSpan RecordedFor { get; init; }
    public ObsRecordingResult Recording { get; init; }
    public bool AlreadyPublished { get; init; }
    public bool AlreadyScheduled { get; init; }
    public bool InOutbox { get; init; }
}

/// <summary>
/// Publication can become eligible only from a verified outcome, a finalized owned path,
/// and duplicate/outbox state. Recording fields stay on the pre-launch decision.
/// </summary>
public static class MediaPolicyPublication
{
    public static ReplayMediaDecision Apply(
        ReplayMediaDecision recorded,
        MediaPublicationFacts facts
    )
    {
        if (recorded == null)
        {
            return Denied(ReplayMediaReason.MissingInput);
        }

        if (facts == null || !CanPromote(recorded))
        {
            return recorded;
        }

        if (facts.AlreadyPublished)
        {
            return Copy(recorded, false, ReplayMediaReason.AlreadyPublished);
        }

        if (facts.AlreadyScheduled)
        {
            return Copy(recorded, false, ReplayMediaReason.AlreadyScheduled);
        }

        if (facts.InOutbox)
        {
            return Copy(recorded, false, ReplayMediaReason.InOutbox);
        }

        if (!IsVerified(facts))
        {
            return Copy(recorded, false, ReplayMediaReason.Incomplete);
        }

        if (!RecordingOwnership.CanPublish(facts.Recording, allowsMedia: true))
        {
            return Copy(recorded, false, MediaReason(facts.Recording));
        }

        return Copy(recorded, true, EligibleReason(recorded));
    }

    private static bool CanPromote(ReplayMediaDecision recorded)
    {
        return recorded.PublicationReason == ReplayMediaReason.AwaitingCompletion
            || recorded.PublicationReason == ReplayMediaReason.AwaitingMedia;
    }

    private static bool IsVerified(MediaPublicationFacts facts)
    {
        if (facts.Outcome != MatchOutcome.VerifiedCompleted || !facts.MatchClockSeen)
        {
            return false;
        }

        return MatchCompletion.AllowsMedia(facts.Outcome, facts.HudSamples, facts.RecordedFor);
    }

    private static string MediaReason(ObsRecordingResult recording)
    {
        if (
            recording != null
            && recording.Finalized
            && !string.IsNullOrWhiteSpace(recording.OutputPath)
            && (!recording.Owned || !recording.Succeeded)
        )
        {
            return ReplayMediaReason.MediaNotCorrelated;
        }

        return ReplayMediaReason.MediaNotFinalized;
    }

    private static string EligibleReason(ReplayMediaDecision recorded)
    {
        if (recorded.Priority == ReplayMediaPriority.Requested)
        {
            return ReplayMediaReason.EligibleRequested;
        }

        if (recorded.PublicationMode == ReplayPublicationMode.AllEligible)
        {
            return ReplayMediaReason.EligibleAll;
        }

        return ReplayMediaReason.EligibleCurated;
    }

    private static ReplayMediaDecision Denied(string reason)
    {
        return new ReplayMediaDecision
        {
            PolicyVersion = ReplayMediaPolicy.PolicyVersion,
            Record = false,
            RecordingReason = reason,
            PublicationCandidate = false,
            PublicationReason = reason,
            Priority = ReplayMediaPriority.Ordinary,
            Score = default,
            NotableEvents = Array.Empty<TeamKillClip>(),
            ConfigurationErrors = Array.Empty<string>(),
            RecordingMode = ReplayRecordingMode.Disabled,
            PublicationMode = ReplayPublicationMode.Disabled,
        };
    }

    private static ReplayMediaDecision Copy(
        ReplayMediaDecision recorded,
        bool publicationCandidate,
        string publicationReason
    )
    {
        return new ReplayMediaDecision
        {
            PolicyVersion = recorded.PolicyVersion,
            ConfigurationVersion = recorded.ConfigurationVersion,
            EvaluatedAtUtc = recorded.EvaluatedAtUtc,
            Record = recorded.Record,
            RecordingReason = recorded.RecordingReason,
            PublicationCandidate = publicationCandidate,
            PublicationReason = publicationReason,
            Priority = recorded.Priority,
            Score = recorded.Score,
            NotableEvents = recorded.NotableEvents ?? Array.Empty<TeamKillClip>(),
            CandidateExpiresAtUtc = recorded.CandidateExpiresAtUtc,
            RecordingMode = recorded.RecordingMode,
            PublicationMode = recorded.PublicationMode,
            SchedulerCurationRequired =
                publicationCandidate && recorded.PublicationMode == ReplayPublicationMode.Curated,
            ConfigurationErrors = recorded.ConfigurationErrors ?? Array.Empty<string>(),
            ReplayId = recorded.ReplayId,
            GameDateUtc = recorded.GameDateUtc,
            GameVersion = recorded.GameVersion,
            Map = recorded.Map,
            GameMode = recorded.GameMode,
            Rank = recorded.Rank,
            AverageMmr = recorded.AverageMmr,
            FocusHero = recorded.FocusHero,
        };
    }
}
