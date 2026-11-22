using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.HeroFocus.Targeted;
using HeroesReplay.Core.YouTube.Metadata;
using HeroesReplay.HeroesProfile.Client;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.HeroFocus.Cards;

/// <summary>
/// Fills the waiting focus cards with each player's Storm League history before the match, for
/// title stories (<c>YouTube:Titles:PlayerStories</c>): one <c>players/matches</c> call per
/// player, from <c>LookbackDays</c> before the match to its date, newest card first. Only the
/// summary (<see cref="PlayerHistory"/>) is kept. A player Heroes Profile reports private, or
/// the privacy feed marks private, gets a private mark and no story. A spent
/// <c>WeeklyCallBudget</c> or a refused key ends the step.
/// </summary>
public sealed class PlayerHistoryRefresh
{
    private readonly HeroesProfileCaller caller;
    private readonly PlayerHeroStatsStore store;
    private readonly PlayerStorySettings settings;
    private readonly TimeSpan jobTimeout;
    private readonly ILogger logger;
    private readonly Func<DateTime> clock;

    public PlayerHistoryRefresh(
        HeroesProfileCaller caller,
        PlayerHeroStatsStore store,
        PlayerStorySettings settings,
        TimeSpan jobTimeout,
        ILogger logger,
        Func<DateTime> clock = null
    )
    {
        this.caller = caller ?? throw new ArgumentNullException(nameof(caller));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.settings = settings ?? new PlayerStorySettings();
        this.jobTimeout = jobTimeout > TimeSpan.Zero ? jobTimeout : TimeSpan.FromMinutes(5);
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.clock = clock ?? (() => DateTime.UtcNow);
    }

    public bool Enabled => settings.Enabled;

    /// <summary>True after a 401 or 403 about the key: nothing more is asked until a restart.</summary>
    public bool Stopped { get; private set; }

    /// <summary>One step over the cards of <paramref name="keys"/>, in order. Returns the calls made.</summary>
    public async Task<int> StepAsync(
        FocusCardStore cards,
        IEnumerable<string> keys,
        CancellationToken cancellationToken
    )
    {
        if (!settings.Enabled || Stopped || cards == null)
        {
            return 0;
        }

        int budget = Math.Max(0, settings.LookupsPerStep);
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

    /// <summary>Fills one card's players in place with at most <paramref name="maxCalls"/> calls.</summary>
    public async Task<FillResult> FillAsync(
        FocusCard card,
        int maxCalls,
        CancellationToken cancellationToken
    )
    {
        int calls = 0;
        bool changed = false;
        if (card?.GameDateUtc is not DateTime played || played == default)
        {
            return new FillResult(0, false, Stop: false);
        }

        foreach (FocusCardPlayer player in Eligible(card))
        {
            if (store.IsPrivate(player.Player, player.RegionId))
            {
                if (player.History?.Status != PlayerHistoryStatus.Private)
                {
                    player.History = Mark(PlayerHistoryStatus.Private);
                    changed = true;
                }

                continue;
            }

            if (!IsDue(player.History) || calls >= maxCalls)
            {
                continue;
            }

            DateTimeOffset now = new(DateTime.SpecifyKind(clock(), DateTimeKind.Utc));
            if (
                store.CallsThisWeek(now, PlayerHeroStatsStore.HistoryCallsFileName)
                >= Math.Max(0, settings.WeeklyCallBudget)
            )
            {
                logger.LogInformation(
                    "Player history lookups paused: {Budget} calls this week. Titles go without player stories until the budget frees up.",
                    settings.WeeklyCallBudget
                );
                return new FillResult(calls, changed, Stop: true);
            }

            store.NoteCall(now, PlayerHeroStatsStore.HistoryCallsFileName);
            calls++;
            HeroesProfileGlobalAnswer answer;
            try
            {
                answer = await caller
                    .GetAsync(
                        PlayerMatches.Path,
                        Query(player.Player, player.RegionId, played, settings.LookbackDays),
                        jobTimeout,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (HeroStatsException e)
            {
                logger.LogInformation(
                    "Heroes Profile had no match history for a {Hero} player this time: {Reason}",
                    player.Hero,
                    e.Message
                );
                player.History = Mark(PlayerHistoryStatus.Unknown);
                changed = true;
                continue;
            }

            switch (answer?.StatusCode)
            {
                case 200:
                    player.History = PlayerHistoryMath.From(
                        PlayerMatches.Parse(answer.Body),
                        card.ReplayId,
                        DateTime.SpecifyKind(played, DateTimeKind.Utc),
                        player.Hero,
                        player.AttributeId,
                        settings,
                        clock()
                    );
                    break;
                case 403
                    when string.Equals(
                        answer.ErrorCode,
                        "player_unavailable",
                        StringComparison.Ordinal
                    ):
                    player.History = Mark(PlayerHistoryStatus.Private);
                    break;
                case 401:
                case 403:
                    Stopped = true;
                    logger.LogWarning(
                        "Heroes Profile refused {Path} ({Status} {Code}). Player history lookups stop until a restart.",
                        PlayerMatches.Path,
                        answer.StatusCode,
                        answer.ErrorCode
                    );
                    return new FillResult(calls, changed, Stop: true);
                default:
                    player.History = Mark(PlayerHistoryStatus.Unknown);
                    break;
            }

            changed = true;
        }

        return new FillResult(calls, changed, Stop: false);
    }

    /// <summary>Never read, or an unknown one past <c>RetryAfter</c>. A known or private history stays.</summary>
    public bool IsDue(PlayerHistory history) =>
        history?.FetchedAtUtc is not DateTime fetched
        || (
            history.Status == PlayerHistoryStatus.Unknown
            && clock() - fetched >= settings.RetryAfter
        );

    public static IReadOnlyList<KeyValuePair<string, string>> Query(
        string battleTag,
        int region,
        DateTime playedAtUtc,
        int lookbackDays
    ) =>
        new[]
        {
            new KeyValuePair<string, string>("battletag", battleTag.Trim()),
            new KeyValuePair<string, string>(
                "region",
                region.ToString(CultureInfo.InvariantCulture)
            ),
            new KeyValuePair<string, string>("game_type", PlayerHeroStatsFetch.StormLeague),
            new KeyValuePair<string, string>(
                "start_date",
                playedAtUtc
                    .AddDays(-Math.Max(1, lookbackDays))
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ),
            new KeyValuePair<string, string>(
                "end_date",
                playedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ),
        };

    public sealed record FillResult(int Calls, bool Changed, bool Stop);

    private PlayerHistory Mark(PlayerHistoryStatus status) =>
        new() { Status = status, FetchedAtUtc = clock() };

    /// <summary>Humans on a catalog hero with a BattleTag Heroes Profile can look up, in slot order.</summary>
    private static IEnumerable<FocusCardPlayer> Eligible(FocusCard card) =>
        (card.Players ?? new List<FocusCardPlayer>())
            .Where(player =>
                !player.IsAi
                && !string.IsNullOrWhiteSpace(player.Hero)
                && PlayerHeroStatsFetch.IsBattleTag(player.Player)
                && PlayerHeroStatsFetch.IsRegion(player.RegionId)
            )
            .OrderBy(player => player.Index);
}
