using System;
using HeroesReplay.Core.MediaPolicy;

namespace HeroesReplay.Core.YouTube.Publication;

/// <summary>
/// What the uploader knows about one replay at send time.
/// A missing or unfinished decision is incomplete, so nothing is sent.
/// </summary>
public sealed class PublicationSendFacts
{
    public ReplayMediaPriority Criteria { get; init; }
    public bool AlreadyPublished { get; init; }
    public bool Incomplete { get; init; }
    public bool Uncorrelated { get; init; }
    public DateTimeOffset? RecordedAtUtc { get; init; }

    public static PublicationSendFacts Unverified(DateTimeOffset? recordedAt)
    {
        return new PublicationSendFacts
        {
            Criteria = ReplayMediaPriority.Ordinary,
            Incomplete = true,
            RecordedAtUtc = recordedAt,
        };
    }

    public static PublicationSendFacts FromDecision(
        ReplayMediaDecision decision,
        DateTimeOffset? recordedAt
    )
    {
        if (decision == null)
        {
            return Unverified(recordedAt);
        }

        string reason = decision.PublicationReason ?? "";
        bool published = reason == ReplayMediaReason.AlreadyPublished;
        bool uncorrelated = reason == ReplayMediaReason.MediaNotCorrelated;
        bool eligible =
            decision.PublicationCandidate
            && (
                reason == ReplayMediaReason.EligibleRequested
                || reason == ReplayMediaReason.EligibleCurated
                || reason == ReplayMediaReason.EligibleAll
            );
        return new PublicationSendFacts
        {
            Criteria = decision.Priority,
            AlreadyPublished = published,
            Uncorrelated = uncorrelated,
            Incomplete = !published && !uncorrelated && !eligible,
            RecordedAtUtc = recordedAt ?? ToOffset(decision.GameDateUtc),
        };
    }

    private static DateTimeOffset? ToOffset(DateTime? played)
    {
        if (played == null || played.Value == default)
        {
            return null;
        }

        DateTime value = played.Value;
        if (value.Kind == DateTimeKind.Local)
        {
            return null;
        }

        if (value.Kind != DateTimeKind.Utc)
        {
            value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        return new DateTimeOffset(value);
    }
}
