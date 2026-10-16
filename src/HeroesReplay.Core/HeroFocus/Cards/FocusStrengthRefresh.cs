using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.HeroesProfile;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.HeroFocus.Cards;

/// <summary>
/// Fills the waiting focus cards with each player's Heroes Profile record on their hero (#433),
/// a few lookups a step: the newest card first and, in a card, the players with the best KDA
/// first, since they are the likeliest focus. A record is asked for again once it is
/// <c>HeroesProfileApi:PlayerStats:MaxAge</c> old (a private one after <c>UnknownRetryAfter</c>,
/// an unread one after <c>Focus:StrengthRetryAfter</c>). A player Heroes Profile now marks
/// private loses the numbers on every card. A spent budget or a refused key ends the step.
/// </summary>
public sealed class FocusStrengthRefresh
{
    private readonly PlayerHeroStatsFetch fetch;
    private readonly HeroFocusSettings focus;
    private readonly PlayerStatsSettings players;
    private readonly ILogger logger;
    private readonly Func<DateTime> clock;

    public FocusStrengthRefresh(
        PlayerHeroStatsFetch fetch,
        HeroFocusSettings focus,
        PlayerStatsSettings players,
        ILogger logger,
        Func<DateTime> clock = null
    )
    {
        this.fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
        this.focus = focus ?? new HeroFocusSettings();
        this.players = players ?? new PlayerStatsSettings();
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// One step over the cards of <paramref name="keys"/>, in that order. Writes each card it
    /// changed and returns the Heroes Profile calls made.
    /// </summary>
    public async Task<int> StepAsync(
        FocusCardStore cards,
        IEnumerable<string> keys,
        CancellationToken cancellationToken
    )
    {
        int budget = Math.Max(0, focus.StrengthLookupsPerStep);
        int calls = 0;
        foreach (string key in keys ?? Enumerable.Empty<string>())
        {
            FocusCard card = cards.Read(key);
            if (card == null)
            {
                continue;
            }

            FillResult result = await FillAsync(card, budget - calls, cancellationToken)
                .ConfigureAwait(false);
            calls += result.Calls;
            if (result.Changed)
            {
                cards.Write(card);
            }

            if (result.Stop || calls >= budget)
            {
                break;
            }
        }

        return calls;
    }

    /// <summary>Fills one card's players in place, making at most <paramref name="maxCalls"/> calls.</summary>
    public async Task<FillResult> FillAsync(
        FocusCard card,
        int maxCalls,
        CancellationToken cancellationToken
    )
    {
        int calls = 0;
        bool changed = false;
        foreach (FocusCardPlayer player in Order(card))
        {
            if (
                player.Strength?.IsKnown == true
                && fetch.Store.IsPrivate(player.Player, player.RegionId)
            )
            {
                player.Strength = new FocusStrength
                {
                    Status = FocusStrengthStatus.Private,
                    FetchedAtUtc = clock(),
                };
                changed = true;
                continue;
            }

            if (!IsDue(player.Strength) || calls >= maxCalls)
            {
                continue;
            }

            PlayerHeroLookup lookup = await fetch
                .LookupAsync(
                    player.Player,
                    player.RegionId,
                    player.Hero,
                    player.AttributeId,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (
                lookup.Status is PlayerHeroLookupStatus.OverBudget or PlayerHeroLookupStatus.Refused
            )
            {
                logger.LogInformation(
                    "Focus player records paused ({Status}); unread records count as unknown.",
                    lookup.Status
                );
                return new FillResult(calls, changed, Stop: true);
            }

            if (!lookup.Cached)
            {
                calls++;
            }

            player.Strength = Strength(lookup, clock());
            changed = true;
        }

        return new FillResult(calls, changed, Stop: false);
    }

    /// <summary>What a lookup means for a card: a record, a private profile, or unknown for now.</summary>
    public static FocusStrength Strength(PlayerHeroLookup lookup, DateTime nowUtc) =>
        lookup?.Status switch
        {
            PlayerHeroLookupStatus.Known when lookup.Stats != null => new FocusStrength
            {
                Status = FocusStrengthStatus.Known,
                Wins = lookup.Stats.Wins,
                Games = lookup.Stats.Games,
                HeroMmr = lookup.Stats.HeroMmr,
                RankTier = lookup.Stats.RankTier,
                FetchedAtUtc = nowUtc,
            },
            PlayerHeroLookupStatus.NoGames => new FocusStrength
            {
                Status = FocusStrengthStatus.Known,
                FetchedAtUtc = nowUtc,
            },
            PlayerHeroLookupStatus.Private => new FocusStrength
            {
                Status = FocusStrengthStatus.Private,
                FetchedAtUtc = nowUtc,
            },
            _ => new FocusStrength { Status = FocusStrengthStatus.Unknown, FetchedAtUtc = nowUtc },
        };

    /// <summary>A record never read, or old enough to read again.</summary>
    public bool IsDue(FocusStrength strength)
    {
        if (strength?.FetchedAtUtc is not DateTime fetched)
        {
            return true;
        }

        TimeSpan age = clock() - fetched;
        return strength.Status switch
        {
            FocusStrengthStatus.Known => age >= players.MaxAge,
            FocusStrengthStatus.Private => age >= players.UnknownRetryAfter,
            _ => age >= focus.StrengthRetryAfter,
        };
    }

    /// <summary>The humans on a catalog hero with a BattleTag, best (kills + assists) per death first.</summary>
    private static IEnumerable<FocusCardPlayer> Order(FocusCard card) =>
        (card?.Players ?? new List<FocusCardPlayer>())
            .Where(player =>
                !player.IsAi
                && !string.IsNullOrWhiteSpace(player.Hero)
                && PlayerHeroStatsFetch.IsBattleTag(player.Player)
                && PlayerHeroStatsFetch.IsRegion(player.RegionId)
            )
            .OrderByDescending(player =>
                (
                    player.Stat(nameof(ScoreResult.SoloKills))
                    + player.Stat(nameof(ScoreResult.Assists))
                ) / Math.Max(1, player.Stat(nameof(ScoreResult.Deaths)))
            )
            .ThenBy(player => player.Index)
            .ToList();

    public sealed record FillResult(int Calls, bool Changed, bool Stop);
}
