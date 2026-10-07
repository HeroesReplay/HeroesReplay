using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.YouTube.Publication;

public sealed class PublicationReservationResult
{
    public string Kind { get; init; }
    public string Reason { get; init; }

    /// <summary>
    /// The slot's publish time for a public listing. Null for a private listing and a refusal.
    /// </summary>
    public DateTimeOffset? PublishAtUtc { get; init; }

    public bool Allow => Kind == PublicationReservation.Granted;
}

/// <summary>
/// One slot is written with a temp file and rename, then read back. A slot's time is when the
/// video publishes, which can be later than the upload, so later replays plan around it. A
/// refusal is not stored as an upload. Stale ordinary work is terminal.
/// </summary>
public static class PublicationReservation
{
    public const string Granted = "granted";
    public const string Refused = "refused";
    public const string Terminal = "terminal";

    public const string FileName = "publication-reservations.txt";
    public const string DryRunFileName = "publication-reservations-dry-run.txt";

    private static readonly object Sync = new();

    /// <summary>The live ledger in <paramref name="dataDirectory"/>, or the dry run's own one.</summary>
    public static string PathFor(string dataDirectory, bool dryRun = false) =>
        string.IsNullOrWhiteSpace(dataDirectory)
            ? null
            : System.IO.Path.Combine(dataDirectory, dryRun ? DryRunFileName : FileName);

    public static PublicationReservationResult TryReserve(
        string path,
        bool publicListing,
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
        PublicationSendFacts facts = null,
        string rank = null,
        IReadOnlyList<string> heroes = null
    )
    {
        if (string.IsNullOrWhiteSpace(path) || !IsWorkKey(workKey))
        {
            return Result(Refused, "work");
        }

        // One uploader can plan two recordings at once. Each plan must see the other's slot.
        lock (Sync)
        {
            Ledger ledger = Read(path);
            if (ledger.Terminal.Contains(workKey))
            {
                return Result(Terminal, "stale");
            }

            if (Holds(ledger, workKey))
            {
                if (Enrich(ledger, workKey, map, rank, hero, heroes))
                {
                    Write(path, ledger);
                }

                return Result(Granted, "reserved", publicListing ? Find(ledger, workKey).At : null);
            }

            List<PublicationSample> slots = PublicationSchedule.History(
                publicAtUtc,
                lastPublicUtc,
                requestedInDay,
                now
            );
            foreach (Slot slot in ledger.Reserved)
            {
                slots.Add(
                    new PublicationSample
                    {
                        At = slot.At,
                        Requested = slot.Requested,
                        Map = slot.Map,
                        Rank = slot.Rank,
                        Hero = slot.Hero,
                        Heroes = slot.Heroes,
                    }
                );
            }

            PublicationSendFacts send =
                facts
                ?? new PublicationSendFacts
                {
                    Criteria = requested
                        ? ReplayMediaPriority.Requested
                        : ReplayMediaPriority.Ordinary,
                    RecordedAtUtc = recordedAtUtc,
                };
            bool isRequest = send.Criteria == ReplayMediaPriority.Requested;
            PublicationDecision decision = PublicationSchedule.Plan(
                settings ?? PublicationSchedule.CanarySettings(),
                send,
                publicListing,
                insertsThisQuotaDay,
                now,
                slots,
                map,
                rank,
                hero,
                heroes,
                PublicationSchedule.Seen(lastMap, lastMapUtc, lastHero, lastHeroUtc)
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
                    At = decision.PublishAtUtc ?? now,
                    Requested = isRequest,
                    Map = BlankToNull(map),
                    Rank = BlankToNull(rank),
                    Hero = BlankToNull(hero),
                    Heroes = CopyHeroes(heroes),
                }
            );
            Write(path, ledger);
            Ledger written = Read(path);
            if (CountKey(written, workKey) != 1)
            {
                return Result(Refused, "conflict");
            }

            return Result(Granted, decision.Reason, decision.PublishAtUtc);
        }
    }

    /// <summary>
    /// The latest slot in the ledger, which is the last scheduled publish time. Null when the
    /// ledger is empty or missing.
    /// </summary>
    public static DateTimeOffset? Latest(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        DateTimeOffset? latest = null;
        lock (Sync)
        {
            foreach (Slot slot in Read(path).Reserved)
            {
                if (latest == null || slot.At > latest.Value)
                {
                    latest = slot.At;
                }
            }
        }

        return latest;
    }

    /// <summary>
    /// Slots whose publish time is after <paramref name="now"/>: videos uploaded, or about to
    /// be, that are not public yet. Zero when the ledger is empty or missing.
    /// </summary>
    public static int CountAfter(string path, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        lock (Sync)
        {
            int count = 0;
            foreach (Slot slot in Read(path).Reserved)
            {
                if (slot.At > now)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Slots whose publish time falls in the <paramref name="window"/> that ends at
    /// <paramref name="now"/>. Every video this uploader published has one, at its publish time.
    /// </summary>
    public static int CountIn(string path, DateTimeOffset now, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        lock (Sync)
        {
            int count = 0;
            foreach (Slot slot in Read(path).Reserved)
            {
                if (slot.At <= now && now - slot.At < window)
                {
                    count++;
                }
            }

            return count;
        }
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

    private static Slot Find(Ledger ledger, string workKey)
    {
        foreach (Slot slot in ledger.Reserved)
        {
            if (string.Equals(slot.WorkKey, workKey, StringComparison.Ordinal))
            {
                return slot;
            }
        }

        return null;
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

    private static PublicationReservationResult Result(
        string kind,
        string reason,
        DateTimeOffset? publishAtUtc = null
    )
    {
        return new PublicationReservationResult
        {
            Kind = kind,
            Reason = reason,
            PublishAtUtc = publishAtUtc,
        };
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
            if ((parts.Length != 4 && parts.Length != 8) || !IsWorkKey(parts[3]))
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

            var slot = new Slot
            {
                At = at,
                Requested = parts[2] == "1",
                WorkKey = parts[3],
            };
            if (parts.Length == 8)
            {
                slot.Map = Unescape(parts[4]);
                slot.Rank = Unescape(parts[5]);
                slot.Hero = Unescape(parts[6]);
                slot.Heroes = SplitHeroes(parts[7]);
            }

            ledger.Reserved.Add(slot);
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
            builder.Append('|');
            builder.Append(Escape(slot.Map));
            builder.Append('|');
            builder.Append(Escape(slot.Rank));
            builder.Append('|');
            builder.Append(Escape(slot.Hero));
            builder.Append('|');
            builder.Append(JoinHeroes(slot.Heroes));
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

    private static bool Enrich(
        Ledger ledger,
        string workKey,
        string map,
        string rank,
        string hero,
        IReadOnlyList<string> heroes
    )
    {
        Slot held = Find(ledger, workKey);
        if (held == null)
        {
            return false;
        }

        bool changed = false;
        if (string.IsNullOrWhiteSpace(held.Map) && !string.IsNullOrWhiteSpace(map))
        {
            held.Map = map.Trim();
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(held.Rank) && !string.IsNullOrWhiteSpace(rank))
        {
            held.Rank = rank.Trim();
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(held.Hero) && !string.IsNullOrWhiteSpace(hero))
        {
            held.Hero = hero.Trim();
            changed = true;
        }

        List<string> incoming = CopyHeroes(heroes);
        if (incoming != null && (held.Heroes == null || incoming.Count > held.Heroes.Count))
        {
            held.Heroes = incoming;
            changed = true;
        }

        return changed;
    }

    private static string BlankToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Escape(string value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : Uri.EscapeDataString(value);
    }

    private static string Unescape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            string text = Uri.UnescapeDataString(value);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static string JoinHeroes(IReadOnlyList<string> heroes)
    {
        if (heroes == null || heroes.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (string hero in heroes)
        {
            if (string.IsNullOrWhiteSpace(hero))
            {
                continue;
            }

            parts.Add(Uri.EscapeDataString(hero.Trim()));
        }

        return string.Join(",", parts);
    }

    private static List<string> SplitHeroes(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var heroes = new List<string>();
        foreach (string part in value.Split(','))
        {
            if (part.Length == 0)
            {
                continue;
            }

            string hero = Unescape(part);
            if (hero != null)
            {
                heroes.Add(hero);
            }
        }

        return heroes.Count == 0 ? null : heroes;
    }

    private static List<string> CopyHeroes(IReadOnlyList<string> heroes)
    {
        if (heroes == null || heroes.Count == 0)
        {
            return null;
        }

        var copy = new List<string>();
        foreach (string hero in heroes)
        {
            if (string.IsNullOrWhiteSpace(hero) || copy.Count >= 16)
            {
                continue;
            }

            string trimmed = hero.Trim();
            if (trimmed.Length > 40)
            {
                trimmed = trimmed.Substring(0, 40);
            }

            copy.Add(trimmed);
        }

        return copy.Count == 0 ? null : copy;
    }

    private sealed class Slot
    {
        public string WorkKey { get; set; }
        public DateTimeOffset At { get; set; }
        public bool Requested { get; set; }
        public string Map { get; set; }
        public string Rank { get; set; }
        public string Hero { get; set; }
        public List<string> Heroes { get; set; }
    }
}
