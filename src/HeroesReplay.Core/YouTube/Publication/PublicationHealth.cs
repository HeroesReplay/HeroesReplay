using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.YouTube.Publication;

public sealed class PublicationHealthReport
{
    public int Pending { get; init; }
    public int Uploaded { get; init; }
    public int Deferred { get; init; }
    public int PublishedDay { get; init; }
    public int PublishedWeek { get; init; }
    public int StuckPrivate { get; init; }
    public int Scheduled { get; init; }
    public string Limit { get; init; }
    public string PolicyVersion { get; init; }
}

/// <summary>
/// Read-only counts from the ledger the caller already has. It does not reserve a slot
/// or call YouTube.
/// </summary>
public static class PublicationHealth
{
    public static PublicationHealthReport Summarize(
        int pending,
        int uploaded,
        int deferred,
        bool quotaExhausted,
        string policyVersion,
        int publishedDay = 0,
        int publishedWeek = 0,
        int stuckPrivate = 0,
        int scheduled = 0
    )
    {
        if (pending < 0)
        {
            pending = 0;
        }

        if (uploaded < 0)
        {
            uploaded = 0;
        }

        if (deferred < 0)
        {
            deferred = 0;
        }

        string limit = "none";
        if (quotaExhausted)
        {
            limit = "quota";
        }
        else if (deferred > 0)
        {
            limit = "publication";
        }

        return new PublicationHealthReport
        {
            Pending = pending,
            Uploaded = uploaded,
            Deferred = deferred,
            Limit = limit,
            PolicyVersion = string.IsNullOrWhiteSpace(policyVersion) ? "1" : policyVersion.Trim(),
            PublishedDay = publishedDay < 0 ? 0 : publishedDay,
            PublishedWeek = publishedWeek < 0 ? 0 : publishedWeek,
            StuckPrivate = stuckPrivate < 0 ? 0 : stuckPrivate,
            Scheduled = scheduled < 0 ? 0 : scheduled,
        };
    }

    public static void WriteStatus(
        string path,
        PublicationHealthReport report,
        IReadOnlyList<string> candidates,
        int reservedCapacity,
        DateTimeOffset? nextSlot
    )
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var builder = new StringBuilder();
        builder.Append("pending=").Append(report?.Pending ?? 0).AppendLine();
        builder.Append("uploaded=").Append(report?.Uploaded ?? 0).AppendLine();
        builder.Append("deferred=").Append(report?.Deferred ?? 0).AppendLine();
        builder.Append("published-day=").Append(report?.PublishedDay ?? 0).AppendLine();
        builder.Append("published-week=").Append(report?.PublishedWeek ?? 0).AppendLine();
        builder.Append("scheduled=").Append(report?.Scheduled ?? 0).AppendLine();
        builder.Append("stuck-private=").Append(report?.StuckPrivate ?? 0).AppendLine();
        builder.Append("limit=").Append(report?.Limit ?? "none").AppendLine();
        builder.Append("policy=").Append(report?.PolicyVersion ?? "1").AppendLine();
        builder.Append("candidates=");
        if (candidates != null)
        {
            bool first = true;
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                builder.Append(candidate.Trim());
            }
        }

        builder.AppendLine();
        int reserved = reservedCapacity < 0 ? 0 : reservedCapacity;
        builder.Append("reserved=").Append(reserved).AppendLine();
        builder.Append("next=");
        if (nextSlot != null)
        {
            builder.Append(nextSlot.Value.ToString("o", CultureInfo.InvariantCulture));
        }

        builder.AppendLine();
        DurableFile.Replace(path, builder.ToString());
    }
}
