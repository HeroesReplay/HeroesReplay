using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Ledger;
using HeroesReplay.Core.HeroFocus.Scoring;
using HeroesReplay.Core.HeroFocus.Signatures;
using HeroesReplay.Core.YouTube.Playlists;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.HeroFocus.Picking;

/// <summary>
/// What the next spectate pick does about focus (#433). <see cref="Order"/> is the waiting
/// replays to try first, best first. <see cref="Reserved"/> is a standout held back from an
/// ordinary pick for the next focus slot.
/// </summary>
public sealed record FocusPlan
{
    public static readonly FocusPlan Off = new();

    /// <summary>A focus replay is due: the cadence is full and no viewer request waits.</summary>
    public bool Due { get; init; }

    /// <summary>Each waiting replay's best eligible player, best first.</summary>
    public IReadOnlyList<FocusCandidate> Ranked { get; init; } = Array.Empty<FocusCandidate>();

    /// <summary>
    /// The replay ids to try first: a pick made before a restart, then, when a focus is due,
    /// <see cref="Ranked"/>'s replays.
    /// </summary>
    public IReadOnlyList<int> Order { get; init; } = Array.Empty<int>();

    public int? Reserved { get; init; }

    /// <summary>
    /// The waiting replays whose card has a Notable event (#510) and whose game is still inside
    /// <c>ReplayMedia:NotableCandidateMaxAge</c>, the one closest to the end of its window first.
    /// They play before the focus plan and the ordinary queue, after a viewer request.
    /// </summary>
    public IReadOnlyList<int> Notable { get; init; } = Array.Empty<int>();
}

/// <summary>
/// The spectator's side of the cycle's focus replays (#433). Every <c>Focus:Every</c>th Standard
/// replay it plays is a focus replay: the waiting replays' focus cards are ranked by
/// <see cref="FocusScorer"/>, the best launchable one is played, and the camera follows that
/// player for the whole match. The ledger (<c>Data\Focus\ledger.json</c>) records the pick, its
/// play or abandonment, and the Standard replays played since the last focus. The spectator is
/// its only writer.
/// <list type="bullet">
/// <item>A viewer request plays first, gets no focus, and does not count.</item>
/// <item>A pick that has not played survives a restart for <c>Focus:PickExpiry</c>; a deferred
/// one is abandoned and the focus stays due.</item>
/// <item>No eligible candidate: an ordinary replay plays and the focus stays due.</item>
/// </list>
/// </summary>
public sealed class FocusPicker
{
    /// <summary>The cadence keeps at most this many ids while no focus plays.</summary>
    internal const int CadenceCap = 100;

    private readonly HeroFocusSettings settings;
    private readonly FocusCardStore cards;
    private readonly HeroBaselineStore baselines;
    private readonly FocusLedgerStore ledger;
    private readonly FocusScorer scorer;
    private readonly string libraryPath;
    private readonly TimeSpan maxAge;
    private readonly TimeSpan notableMaxAge;
    private readonly ILogger<FocusPicker> logger;
    private readonly Func<DateTime> clock;

    public FocusPicker(
        HeroFocusSettings settings,
        string dataDirectory,
        TimeSpan maxAge,
        HeroSignatures signatures,
        ILogger<FocusPicker> logger,
        Func<DateTime> clock = null,
        TimeSpan? notableMaxAge = null
    )
    {
        this.settings = settings ?? new HeroFocusSettings();
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.clock = clock ?? (() => DateTime.UtcNow);
        this.maxAge = maxAge;
        this.notableMaxAge = notableMaxAge ?? TimeSpan.FromDays(7);
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return;
        }

        cards = new FocusCardStore(FocusCardStore.DirectoryFor(dataDirectory));
        baselines = new HeroBaselineStore(HeroBaselineStore.PathFor(dataDirectory));
        ledger = new FocusLedgerStore(FocusLedgerStore.PathFor(dataDirectory));
        scorer = new FocusScorer(
            this.settings,
            signatures ?? throw new ArgumentNullException(nameof(signatures))
        );
        libraryPath = YouTubeLibraryRecord.PathFor(dataDirectory);
    }

    /// <summary><c>Focus:Enabled</c> and a data folder to keep the ledger in.</summary>
    public bool Enabled => settings.Enabled && ledger != null;

    public string LedgerPath => ledger?.FilePath;

    /// <summary>
    /// Decides the next pick. <paramref name="waiting"/> holds the Standard replay ids that may
    /// be picked; <paramref name="requestWaiting"/> is true while a viewer request waits.
    /// </summary>
    public FocusPlan Plan(IReadOnlyCollection<int> waiting, bool requestWaiting)
    {
        if (!Enabled || waiting == null || waiting.Count == 0)
        {
            return FocusPlan.Off;
        }

        DateTime now = clock();
        FocusLedger current = ExpireOpenPicks(now);
        var ids = waiting.ToHashSet();
        List<int> open = (current.Entries ?? new List<FocusLedgerEntry>())
            .Where(entry =>
                entry.State == FocusEntryState.Picked
                && entry.ReplayId is int id
                && ids.Contains(id)
            )
            .Select(entry => entry.ReplayId.Value)
            .Distinct()
            .ToList();
        bool due =
            !requestWaiting && (current.SinceFocus?.Count ?? 0) >= Math.Max(1, settings.Every) - 1;

        IReadOnlyList<FocusCard> waitingCards = cards
            .ReadAll()
            .Where(card => card.ReplayId is int id && ids.Contains(id))
            .ToList();
        IReadOnlyList<int> notable = NotableFirst(waitingCards, now);
        List<FocusCard> pool = waitingCards.Where(card => Fresh(card, now)).ToList();
        IReadOnlyList<FocusCandidate> ranking =
            pool.Count == 0
                ? Array.Empty<FocusCandidate>()
                : scorer.Rank(
                    pool,
                    current,
                    baselines.Read(),
                    now,
                    FocusCoverage.PublishedReplayIds(libraryPath)
                );
        List<FocusCandidate> best = ranking
            .Where(candidate => candidate.Eligible && candidate.ReplayId != null)
            .GroupBy(candidate => candidate.ReplayId.Value)
            .Select(group => group.First())
            .ToList();

        if (!due)
        {
            FocusCandidate standout = best.FirstOrDefault(candidate =>
                candidate.Novelty >= settings.ReserveNoveltyAt
            );
            return new FocusPlan
            {
                Ranked = best,
                Order = open,
                Reserved = standout?.ReplayId,
                Notable = notable,
            };
        }

        if (best.Count == 0 && open.Count == 0)
        {
            logger.LogInformation(
                "hero_focus.no_candidate: a focus replay is due, but none of the {Pool} waiting replays with a focus card has an eligible player ({Gated}). An ordinary replay plays and the focus stays due.",
                pool.Count,
                Gated(ranking)
            );
        }
        else if (best.Count > 0)
        {
            FocusCandidate top = best[0];
            logger.LogInformation(
                "A focus replay is due: {Eligible} of {Pool} waiting replays have an eligible player. Best: {Hero} ({Role}) in replay {ReplayId}, score {Score} (quality {Quality}, novelty {Novelty}).",
                best.Count,
                pool.Count,
                FocusPick.Describe(top.Hero, top.BuildClear ? top.Build : null),
                top.Role,
                top.ReplayId,
                top.Score,
                top.Quality,
                top.Novelty
            );
        }

        return new FocusPlan
        {
            Due = true,
            Ranked = best,
            Order = open.Concat(best.Select(candidate => candidate.ReplayId.Value))
                .Distinct()
                .ToList(),
            Notable = notable,
        };
    }

    /// <summary>
    /// The waiting replays with a Notable event (#510) that can still be recorded as Notable:
    /// the oldest game first, since its window closes soonest. A card without a game date is
    /// inside the window, as the media policy reads it, and comes last.
    /// </summary>
    private IReadOnlyList<int> NotableFirst(IEnumerable<FocusCard> waitingCards, DateTime now) =>
        waitingCards
            .Where(card =>
                card.Notable is { Count: > 0 }
                && (card.GameDateUtc is not DateTime played || now - played <= notableMaxAge)
            )
            .OrderBy(card => card.GameDateUtc ?? DateTime.MaxValue)
            .ThenByDescending(card => card.ReplayId)
            .Select(card => card.ReplayId.Value)
            .ToList();

    /// <summary>
    /// The focus of the replay the spectator just took, or null. A pick made before a restart
    /// is kept; otherwise, when <paramref name="plan"/> is due and ranked this replay, its best
    /// player becomes the pick and the ledger records it. Either way the parsed replay must still
    /// have that player at that index. A request never gets a focus.
    /// </summary>
    public FocusPick Attach(int replayId, Replay replay, bool requested, FocusPlan plan)
    {
        if (!Enabled || requested || replayId <= 0)
        {
            return null;
        }

        DateTime now = clock();
        string key = Key(replayId);
        FocusLedgerEntry open = ledger.Read().Find(key);
        if (open?.State == FocusEntryState.Picked && !Expired(open, now))
        {
            var kept = new FocusPick(
                replayId,
                key,
                open.PlayerIndex,
                open.Player,
                open.Hero,
                open.AttributeId,
                open.Role,
                open.BuildClear ? open.Build : null
            );
            if (!Matches(replay, kept))
            {
                Abandon(kept, "its player is not at that index in the parsed replay");
                return null;
            }

            logger.LogInformation(
                "Replay {ReplayId} keeps its focus pick from {PickedAt:o}: following {Focus}.",
                replayId,
                open.PickedAtUtc,
                kept.Label
            );
            return kept;
        }

        FocusCandidate candidate =
            plan?.Due == true
                ? plan.Ranked.FirstOrDefault(ranked => ranked.ReplayId == replayId)
                : null;
        if (candidate == null)
        {
            return null;
        }

        var pick = new FocusPick(
            replayId,
            candidate.Key ?? key,
            candidate.PlayerIndex,
            candidate.Player,
            candidate.Hero,
            candidate.AttributeId,
            candidate.Role,
            candidate.BuildClear ? candidate.Build : null
        );
        if (!Matches(replay, pick))
        {
            logger.LogWarning(
                "Replay {ReplayId}'s focus card does not match the parsed replay at player {Index}. It plays as an ordinary replay.",
                replayId,
                pick.PlayerIndex
            );
            return null;
        }

        ledger.Update(entries =>
        {
            entries.Put(candidate.ToEntry(FocusEntryState.Picked, now));
            return true;
        });
        logger.LogInformation(
            "Replay {ReplayId} is a focus replay: the camera follows {Focus}, a {Role}, for the whole match (score {Score}, quality {Quality}, novelty {Novelty}).",
            replayId,
            pick.Label,
            pick.Role,
            candidate.Score,
            candidate.Quality,
            candidate.Novelty
        );
        return pick;
    }

    /// <summary>
    /// A replay played to the end. A focus replay is Played and starts the count again; any other
    /// Standard replay counts toward the next focus, once however often it is marked. A request
    /// does not count.
    /// </summary>
    public void Played(int replayId, FocusPick pick, bool requested)
    {
        if (!Enabled || replayId <= 0 || (pick == null && requested))
        {
            return;
        }

        DateTime now = clock();
        ledger.Update(entries =>
        {
            if (pick != null)
            {
                FocusLedgerEntry entry = entries.Find(pick.Key);
                if (entry != null)
                {
                    entry.State = FocusEntryState.Played;
                    entry.PlayedAtUtc = now;
                }

                entries.SinceFocus.Clear();
                return true;
            }

            // A pick that played without its focus (resumed after an outage) covered nothing.
            FocusLedgerEntry open = entries.Find(Key(replayId));
            if (open?.State == FocusEntryState.Picked)
            {
                open.State = FocusEntryState.Abandoned;
            }

            entries.SinceFocus ??= new List<int>();
            if (!entries.SinceFocus.Contains(replayId))
            {
                entries.SinceFocus.Add(replayId);
                if (entries.SinceFocus.Count > CadenceCap)
                {
                    entries.SinceFocus.RemoveRange(0, entries.SinceFocus.Count - CadenceCap);
                }
            }

            return true;
        });
    }

    /// <summary>A focus replay left the queue before it played: the focus stays due.</summary>
    public void Abandon(FocusPick pick, string reason)
    {
        if (!Enabled || pick == null)
        {
            return;
        }

        bool changed = ledger.Update(entries =>
        {
            FocusLedgerEntry entry = entries.Find(pick.Key);
            if (entry?.State != FocusEntryState.Picked)
            {
                return false;
            }

            entry.State = FocusEntryState.Abandoned;
            return true;
        });
        if (changed)
        {
            logger.LogInformation(
                "Focus pick {Focus} in replay {ReplayId} is abandoned: {Reason}. The focus stays due.",
                pick.Label,
                pick.ReplayId,
                reason
            );
        }
    }

    public static string Key(int replayId) => replayId.ToString(CultureInfo.InvariantCulture);

    private FocusLedger ExpireOpenPicks(DateTime now)
    {
        FocusLedger read = ledger.Read();
        if (read.Entries?.Any(entry => Expired(entry, now)) != true)
        {
            return read;
        }

        return ledger.Update(entries =>
        {
            foreach (FocusLedgerEntry entry in entries.Entries.Where(entry => Expired(entry, now)))
            {
                entry.State = FocusEntryState.Abandoned;
                logger.LogInformation(
                    "Focus pick {Focus} in replay {ReplayId} from {PickedAt:o} did not play within {Expiry}. It is abandoned.",
                    FocusPick.Describe(entry.Hero, entry.BuildClear ? entry.Build : null),
                    entry.ReplayId,
                    entry.PickedAtUtc,
                    settings.PickExpiry
                );
            }

            return entries;
        });
    }

    private bool Expired(FocusLedgerEntry entry, DateTime now) =>
        entry.State == FocusEntryState.Picked && now - entry.PickedAtUtc >= settings.PickExpiry;

    private bool Fresh(FocusCard card, DateTime now) =>
        card.GameDateUtc is not DateTime played || now - played <= maxAge;

    /// <summary>The parsed replay still has the pick's <c>Name#1234</c> at its index.</summary>
    private static bool Matches(Replay replay, FocusPick pick)
    {
        Player[] players = replay?.Players;
        if (
            players == null
            || string.IsNullOrWhiteSpace(pick.Player)
            || pick.PlayerIndex < 0
            || pick.PlayerIndex >= players.Length
        )
        {
            return false;
        }

        Player player = players[pick.PlayerIndex];
        return !string.IsNullOrWhiteSpace(player?.Name)
            && string.Equals(
                $"{player.Name}#{player.BattleTag}",
                pick.Player,
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static string Gated(IReadOnlyList<FocusCandidate> ranking)
    {
        List<string> counts = ranking
            .Where(candidate => !candidate.Eligible)
            .GroupBy(candidate => candidate.Gate)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}={group.Count()}")
            .ToList();
        return counts.Count == 0 ? "no cards" : string.Join(", ", counts);
    }
}
