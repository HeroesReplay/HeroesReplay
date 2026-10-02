using System;
using System.Collections.Generic;
using HeroesReplay.Core.Analysis;

namespace HeroesReplay.Core.MediaPolicy;

public readonly record struct ReplayMediaScore(
    int PriorityWeight,
    long Recency,
    long NotableStrength,
    long Skill,
    long Total,
    int TieBreakReplayId,
    long TieBreakGameDateTicks
);

public sealed class ReplayMediaDecision
{
    public string PolicyVersion { get; init; }
    public string ConfigurationVersion { get; init; }
    public DateTime EvaluatedAtUtc { get; init; }
    public bool Record { get; init; }
    public string RecordingReason { get; init; }
    public bool PublicationCandidate { get; init; }
    public string PublicationReason { get; init; }
    public ReplayMediaPriority Priority { get; init; }
    public ReplayMediaScore Score { get; init; }
    public IReadOnlyList<TeamKillClip> NotableEvents { get; init; }
    public DateTime? CandidateExpiresAtUtc { get; init; }
    public ReplayRecordingMode RecordingMode { get; init; }
    public ReplayPublicationMode PublicationMode { get; init; }
    public bool SchedulerCurationRequired { get; init; }
    public IReadOnlyList<string> ConfigurationErrors { get; init; }
    public int? ReplayId { get; init; }
    public DateTime? GameDateUtc { get; init; }
    public string GameVersion { get; init; }
    public string Map { get; init; }
    public string GameMode { get; init; }
    public string Rank { get; init; }
    public double? AverageMmr { get; init; }
    public string FocusHero { get; init; }
}
