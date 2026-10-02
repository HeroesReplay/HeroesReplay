using System;

namespace HeroesReplay.Core.YouTube.Publication;

public sealed class PublicationDecision
{
    public bool Allow { get; init; }
    public string Reason { get; init; }
    public int Penalty { get; init; }

    /// <summary>
    /// The time YouTube should publish the video. Null for a private listing and for a refusal.
    /// </summary>
    public DateTimeOffset? PublishAtUtc { get; init; }

    public static PublicationDecision Refused(string reason) =>
        new()
        {
            Allow = false,
            Reason = reason,
            Penalty = 0,
        };

    public static PublicationDecision Granted(string reason) =>
        new()
        {
            Allow = true,
            Reason = reason,
            Penalty = 0,
        };

    public static PublicationDecision Scheduled(string reason, DateTimeOffset publishAtUtc) =>
        new()
        {
            Allow = true,
            Reason = reason,
            Penalty = 0,
            PublishAtUtc = publishAtUtc,
        };
}
