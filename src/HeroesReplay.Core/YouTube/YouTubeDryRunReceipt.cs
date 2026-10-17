using System;

namespace HeroesReplay.Core.YouTube;

/// <summary>
/// The dry-run receipt beside a recording (<c>DryRunRecordings.PlanFileName</c>): the plan a
/// live send would follow. Its names are the ones the receipt had as an anonymous type.
/// </summary>
internal sealed record YouTubeDryRunReceipt(
    string Title,
    string PrivacyStatus,
    string DesiredPrivacyStatus,
    DateTimeOffset? PublishAtUtc,
    string Schedule,
    string ScheduleReason,
    bool? SelfDeclaredMadeForKids,
    string CategoryId,
    long Bytes,
    string Recording,
    bool Simulated
);
