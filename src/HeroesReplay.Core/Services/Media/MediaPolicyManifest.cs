using System;
using System.Collections.Generic;
using HeroesReplay.Core.Services.Analysis;
using HeroesReplay.Core.Services.YouTube.Outbox;

namespace HeroesReplay.Core.Services.Media;

public static class MediaPolicyManifest
{
    public static UploadAttemptPolicy FromDecision(
        ReplayMediaDecision decision,
        bool publicationEvaluated
    )
    {
        if (decision == null)
        {
            return null;
        }

        return new UploadAttemptPolicy
        {
            PolicyVersion = decision.PolicyVersion,
            ConfigurationVersion = decision.ConfigurationVersion,
            Record = decision.Record,
            RecordingReason = decision.RecordingReason,
            PublicationCandidate = decision.PublicationCandidate,
            PublicationReason = decision.PublicationReason,
            Priority = decision.Priority.ToString(),
            RecordingMode = decision.RecordingMode.ToString(),
            PublicationMode = decision.PublicationMode.ToString(),
            Score = new UploadAttemptScoreEvidence
            {
                PriorityWeight = decision.Score.PriorityWeight,
                Recency = decision.Score.Recency,
                NotableStrength = decision.Score.NotableStrength,
                Skill = decision.Score.Skill,
                Total = decision.Score.Total,
                TieBreakReplayId = decision.Score.TieBreakReplayId,
                TieBreakGameDateTicks = decision.Score.TieBreakGameDateTicks,
            },
            ExpiresAtUtc = AsUtc(decision.CandidateExpiresAtUtc),
            EvaluatedAtUtc = AsUtc(decision.EvaluatedAtUtc),
            PublicationEvaluated = publicationEvaluated,
            ReplayId = decision.ReplayId,
            GameDateUtc = AsUtc(decision.GameDateUtc),
            GameVersion = decision.GameVersion,
            Map = decision.Map,
            GameMode = decision.GameMode,
            Rank = decision.Rank,
            AverageMmr = decision.AverageMmr,
            FocusHero = decision.FocusHero,
        };
    }

    public static ReplayMediaDecision ToDecision(UploadAttemptManifest manifest)
    {
        UploadAttemptPolicy policy = manifest?.Policy;
        if (policy == null || !TryRead(policy, out ReplayMediaDecision decision))
        {
            return null;
        }

        if (decision.ReplayId == null)
        {
            return new ReplayMediaDecision
            {
                PolicyVersion = decision.PolicyVersion,
                ConfigurationVersion = decision.ConfigurationVersion,
                EvaluatedAtUtc = decision.EvaluatedAtUtc,
                Record = decision.Record,
                RecordingReason = decision.RecordingReason,
                PublicationCandidate = decision.PublicationCandidate,
                PublicationReason = decision.PublicationReason,
                Priority = decision.Priority,
                Score = decision.Score,
                NotableEvents = decision.NotableEvents,
                CandidateExpiresAtUtc = decision.CandidateExpiresAtUtc,
                RecordingMode = decision.RecordingMode,
                PublicationMode = decision.PublicationMode,
                SchedulerCurationRequired = decision.SchedulerCurationRequired,
                ConfigurationErrors = decision.ConfigurationErrors,
                ReplayId = manifest.ReplayId,
                GameDateUtc = decision.GameDateUtc,
                GameVersion = decision.GameVersion,
                Map = decision.Map,
                GameMode = decision.GameMode,
                Rank = decision.Rank,
                AverageMmr = decision.AverageMmr,
                FocusHero = decision.FocusHero,
            };
        }

        return decision;
    }

    private static bool TryRead(UploadAttemptPolicy policy, out ReplayMediaDecision decision)
    {
        decision = null;
        if (
            string.IsNullOrWhiteSpace(policy.PolicyVersion)
            || string.IsNullOrWhiteSpace(policy.RecordingReason)
            || string.IsNullOrWhiteSpace(policy.PublicationReason)
            || policy.Score == null
            || !TryNamedEnum(policy.Priority, out ReplayMediaPriority priority)
            || !TryNamedEnum(policy.RecordingMode, out ReplayRecordingMode recording)
            || !TryNamedEnum(policy.PublicationMode, out ReplayPublicationMode publication)
        )
        {
            return false;
        }

        bool candidate = policy.PublicationCandidate;
        decision = new ReplayMediaDecision
        {
            PolicyVersion = policy.PolicyVersion.Trim(),
            ConfigurationVersion = string.IsNullOrWhiteSpace(policy.ConfigurationVersion)
                ? null
                : policy.ConfigurationVersion.Trim(),
            EvaluatedAtUtc = AsUtc(policy.EvaluatedAtUtc) ?? default,
            Record = policy.Record,
            RecordingReason = policy.RecordingReason.Trim(),
            PublicationCandidate = candidate,
            PublicationReason = policy.PublicationReason.Trim(),
            Priority = priority,
            Score = new ReplayMediaScore(
                policy.Score.PriorityWeight,
                policy.Score.Recency,
                policy.Score.NotableStrength,
                policy.Score.Skill,
                policy.Score.Total,
                policy.Score.TieBreakReplayId,
                policy.Score.TieBreakGameDateTicks
            ),
            NotableEvents = Array.Empty<TeamKillClip>(),
            CandidateExpiresAtUtc = AsUtc(policy.ExpiresAtUtc),
            RecordingMode = recording,
            PublicationMode = publication,
            SchedulerCurationRequired = candidate && publication == ReplayPublicationMode.Curated,
            ConfigurationErrors = Array.Empty<string>(),
            ReplayId = policy.ReplayId is int id && id > 0 ? id : null,
            GameDateUtc = AsUtc(policy.GameDateUtc),
            GameVersion = Trim(policy.GameVersion),
            Map = Trim(policy.Map),
            GameMode = Trim(policy.GameMode),
            Rank = Trim(policy.Rank),
            AverageMmr = policy.AverageMmr,
            FocusHero = Trim(policy.FocusHero),
        };
        return true;
    }

    private static bool TryNamedEnum<TEnum>(string text, out TEnum value)
        where TEnum : struct
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (!Enum.TryParse(trimmed, ignoreCase: false, out value))
        {
            return false;
        }

        return Enum.IsDefined(typeof(TEnum), value)
            && string.Equals(trimmed, value.ToString(), StringComparison.Ordinal);
    }

    private static DateTime? AsUtc(DateTime? value)
    {
        if (value == null)
        {
            return null;
        }

        DateTime time = value.Value;
        if (time.Kind == DateTimeKind.Utc)
        {
            return time;
        }

        if (time.Kind == DateTimeKind.Local)
        {
            return time.ToUniversalTime();
        }

        return DateTime.SpecifyKind(time, DateTimeKind.Utc);
    }

    private static string Trim(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
