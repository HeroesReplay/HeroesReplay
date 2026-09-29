using System;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Services.YouTube.Outbox;

/// <summary>
/// Observe-only recording and publication decision stored in attempt-manifest.json.
/// A true record flag is not proof that OBS started or finalized a file.
/// </summary>
public sealed class UploadAttemptPolicy
{
    [JsonPropertyName("PolicyVersion")]
    public string PolicyVersion { get; init; }

    [JsonPropertyName("ConfigurationVersion")]
    public string ConfigurationVersion { get; init; }

    [JsonPropertyName("Record")]
    public bool Record { get; init; }

    [JsonPropertyName("RecordingReason")]
    public string RecordingReason { get; init; }

    [JsonPropertyName("PublicationCandidate")]
    public bool PublicationCandidate { get; init; }

    [JsonPropertyName("PublicationReason")]
    public string PublicationReason { get; init; }

    [JsonPropertyName("Priority")]
    public string Priority { get; init; }

    [JsonPropertyName("RecordingMode")]
    public string RecordingMode { get; init; }

    [JsonPropertyName("PublicationMode")]
    public string PublicationMode { get; init; }

    [JsonPropertyName("Score")]
    public UploadAttemptScoreEvidence Score { get; init; }

    [JsonPropertyName("ExpiresAtUtc")]
    public DateTime? ExpiresAtUtc { get; init; }

    [JsonPropertyName("EvaluatedAtUtc")]
    public DateTime? EvaluatedAtUtc { get; init; }

    [JsonPropertyName("PublicationEvaluated")]
    public bool PublicationEvaluated { get; init; }

    [JsonPropertyName("ReplayId")]
    public int? ReplayId { get; init; }

    [JsonPropertyName("GameDateUtc")]
    public DateTime? GameDateUtc { get; init; }

    [JsonPropertyName("GameVersion")]
    public string GameVersion { get; init; }

    [JsonPropertyName("Map")]
    public string Map { get; init; }

    [JsonPropertyName("GameMode")]
    public string GameMode { get; init; }

    [JsonPropertyName("Rank")]
    public string Rank { get; init; }

    [JsonPropertyName("AverageMmr")]
    public double? AverageMmr { get; init; }

    [JsonPropertyName("FocusHero")]
    public string FocusHero { get; init; }
}

public sealed class UploadAttemptScoreEvidence
{
    [JsonPropertyName("PriorityWeight")]
    public int PriorityWeight { get; init; }

    [JsonPropertyName("Recency")]
    public long Recency { get; init; }

    [JsonPropertyName("NotableStrength")]
    public long NotableStrength { get; init; }

    [JsonPropertyName("Skill")]
    public long Skill { get; init; }

    [JsonPropertyName("Total")]
    public long Total { get; init; }

    [JsonPropertyName("TieBreakReplayId")]
    public int TieBreakReplayId { get; init; }

    [JsonPropertyName("TieBreakGameDateTicks")]
    public long TieBreakGameDateTicks { get; init; }
}
