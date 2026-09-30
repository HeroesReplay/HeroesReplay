using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HeroesReplay.Core.Services.Media;
using HeroesReplay.Core.Services.Queue;

namespace HeroesReplay.Core.Services.YouTube;

public sealed class PublicationReservationResult
{
    public string Kind { get; init; }
    public string Reason { get; init; }

    public bool Allow => Kind == PublicationReservation.Granted;
}

/// <summary>
/// One slot is written with a temp file and rename, then read back.
/// A schedule refusal is not stored as an upload. Stale ordinary work is terminal.
/// </summary>
public static class PublicationReservation
{
    public const string Granted = "granted";
    public const string Refused = "refused";
    public const string Terminal = "terminal";

    public static PublicationReservationResult TryReserve(
        string path,
        bool productionHost,
        int insertsThisQuotaDay,
        DateTimeOffset now,
        DateTimeOffset? lastPublicUtc,
        IReadOnlyList<DateTimeOffset> publicAtUtc,
        int requestedInDay,
        bool requested,
        DateTimeOffset? recordedAtUtc,
        string map,
        string lastMap,
        DateTimeOffset? lastMapUtc,
        string hero,
        string lastHero,
        DateTimeOffset? lastHeroUtc,
        string workKey,
        ReplayMediaPolicySettings settings = null,
        PublicationSendFacts facts = null
    )
    {
        if (string.IsNullOrWhiteSpace(path) || !IsWorkKey(workKey))
        {
            return Result(Refused, "work");
        }

        Ledger ledger = Read(path);
        if (ledger.Terminal.Contains(workKey))
        {
            return Result(Terminal, "stale");
        }

        if (Holds(ledger, workKey))
        {
            return Result(Granted, "reserved");
        }

        var times = new List<DateTimeOffset>();
        if (publicAtUtc != null)
        {
            times.AddRange(publicAtUtc);
        }

        DateTimeOffset? last = lastPublicUtc;
        int reservedRequests = 0;
        foreach (Slot slot in ledger.Reserved)
        {
            times.Add(slot.At);
            if (last == null || slot.At > last.Value)
            {
                last = slot.At;
            }

            if (slot.Requested && now - slot.At < TimeSpan.FromHours(24) && now >= slot.At)
            {
                reservedRequests++;
            }
        }

        PublicationSendFacts send =
            facts
            ?? new PublicationSendFacts
            {
                Criteria = requested ? ReplayMediaPriority.Requested : ReplayMediaPriority.Ordinary,
                RecordedAtUtc = recordedAtUtc,
            };
        bool isRequest = send.Criteria == ReplayMediaPriority.Requested;
        PublicationDecision decision = PublicationSchedule.Decide(
            settings ?? PublicationSchedule.CanarySettings(),
            send,
            productionHost,
            insertsThisQuotaDay,
            now,
            last,
            times,
            requestedInDay + reservedRequests,
            map,
            lastMap,
            lastMapUtc,
            hero,
            lastHero,
            lastHeroUtc
        );
        if (!decision.Allow)
        {
            if (decision.Reason == "stale" && !isRequest)
            {
                ledger.Terminal.Add(workKey);
                Write(path, ledger);
                Ledger saved = Read(path);
                if (!saved.Terminal.Contains(workKey))
                {
                    return Result(Refused, "stale");
                }

                return Result(Terminal, "stale");
            }

            return Result(Refused, decision.Reason);
        }

        ledger.Reserved.Add(
            new Slot
            {
                WorkKey = workKey,
                At = now,
                Requested = isRequest,
            }
        );
        Write(path, ledger);
        Ledger written = Read(path);
        if (CountKey(written, workKey) != 1)
        {
            return Result(Refused, "conflict");
        }

        return Result(Granted, decision.Reason);
    }

    public static bool IsWorkKey(string workKey)
    {
        if (string.IsNullOrWhiteSpace(workKey) || workKey.Length > 80)
        {
            return false;
        }

        foreach (char character in workKey)
        {
            bool allowed =
                (character >= 'a' && character <= 'z')
                || (character >= 'A' && character <= 'Z')
                || (character >= '0' && character <= '9')
                || character == '-';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Holds(Ledger ledger, string workKey)
    {
        return CountKey(ledger, workKey) == 1;
    }

    private static int CountKey(Ledger ledger, string workKey)
    {
        int count = 0;
        foreach (Slot slot in ledger.Reserved)
        {
            if (string.Equals(slot.WorkKey, workKey, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static PublicationReservationResult Result(string kind, string reason)
    {
        return new PublicationReservationResult { Kind = kind, Reason = reason };
    }

    private static Ledger Read(string path)
    {
        var ledger = new Ledger();
        string text = DurableFile.ReadOrAside(path);
        if (string.IsNullOrEmpty(text))
        {
            return ledger;
        }

        string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.StartsWith("terminal|", StringComparison.Ordinal))
            {
                string key = line.Substring("terminal|".Length);
                if (IsWorkKey(key))
                {
                    ledger.Terminal.Add(key);
                }

                continue;
            }

            if (!line.StartsWith("reserved|", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split('|');
            if (parts.Length != 4 || !IsWorkKey(parts[3]))
            {
                continue;
            }

            if (
                !DateTimeOffset.TryParse(
                    parts[1],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset at
                )
                || at.Offset != TimeSpan.Zero
            )
            {
                continue;
            }

            ledger.Reserved.Add(
                new Slot
                {
                    At = at,
                    Requested = parts[2] == "1",
                    WorkKey = parts[3],
                }
            );
        }

        return ledger;
    }

    private static void Write(string path, Ledger ledger)
    {
        var builder = new StringBuilder();
        foreach (Slot slot in ledger.Reserved)
        {
            builder.Append("reserved|");
            builder.Append(slot.At.ToString("o", CultureInfo.InvariantCulture));
            builder.Append('|');
            builder.Append(slot.Requested ? '1' : '0');
            builder.Append('|');
            builder.Append(slot.WorkKey);
            builder.AppendLine();
        }

        foreach (string key in ledger.Terminal)
        {
            builder.Append("terminal|");
            builder.Append(key);
            builder.AppendLine();
        }

        DurableFile.Replace(path, builder.ToString());
    }

    private sealed class Ledger
    {
        public List<Slot> Reserved { get; } = new();
        public HashSet<string> Terminal { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Slot
    {
        public string WorkKey { get; init; }
        public DateTimeOffset At { get; init; }
        public bool Requested { get; init; }
    }
}
