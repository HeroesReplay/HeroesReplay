using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Ledger;
using HeroesReplay.Core.Shared;
using HeroesReplay.HeroesProfile.Client;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.HeroFocus.Targeted;

/// <summary>What one targeted step did.</summary>
public enum TargetedStep
{
    Off,
    NotDue,
    EnoughWaiting,
    OverBudget,
    NoTarget,
    NoStrongPlayer,
    NoGame,
    Fetched,
}

/// <summary>
/// The download role's fetch of a game for a hero the cycle has not covered (#433). Every
/// <c>Focus:Targeted:Interval</c>, when fewer than <c>MaxWaiting</c> fetched replays wait, it takes
/// the hero that has gone longest without a focus and without showing on a waiting replay (the
/// least-covered role first). Heroes Profile's leaderboard for that hero, merged over the last
/// <c>Seasons</c> seasons, names players with at least <c>Focus:MinHeroGames</c> games and
/// <c>Focus:MinWinRate</c>; their newest won games on the hero within <c>Lookback</c> are tried in
/// turn, and the first one spectate can launch is downloaded into <c>Data\Standard</c>. Its card
/// is built like any other, and an uncovered hero's strong player is the reserved standout for
/// the next focus slot. Nothing happens before the pool has had <c>After</c> to show a hero.
/// </summary>
public sealed class FocusTargetedFetch
{
    private static readonly string[] Roles =
    {
        "Tank",
        "Bruiser",
        "Healer",
        "Support",
        "Melee Assassin",
        "Ranged Assassin",
    };

    private readonly HeroesProfileCaller caller;
    private readonly FocusTargetedStore store;
    private readonly FocusLedgerStore ledger;
    private readonly HeroFocusSettings settings;
    private readonly Func<IReadOnlyList<Hero>> roster;
    private readonly Func<int, CancellationToken, Task<TargetedDownload>> download;
    private readonly TimeSpan jobTimeout;
    private readonly ILogger<FocusTargetedFetch> logger;
    private readonly Func<DateTime> clock;
    private bool budgetWarned;

    public FocusTargetedFetch(
        HeroesProfileCaller caller,
        FocusTargetedStore store,
        FocusLedgerStore ledger,
        HeroFocusSettings settings,
        Func<IReadOnlyList<Hero>> roster,
        Func<int, CancellationToken, Task<TargetedDownload>> download,
        TimeSpan jobTimeout,
        ILogger<FocusTargetedFetch> logger,
        Func<DateTime> clock = null
    )
    {
        this.caller = caller ?? throw new ArgumentNullException(nameof(caller));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        this.settings = settings ?? new HeroFocusSettings();
        this.roster = roster ?? throw new ArgumentNullException(nameof(roster));
        this.download = download ?? throw new ArgumentNullException(nameof(download));
        this.jobTimeout = jobTimeout > TimeSpan.Zero ? jobTimeout : TimeSpan.FromMinutes(5);
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.clock = clock ?? (() => DateTime.UtcNow);
    }

    public FocusTargetedStore Store => store;

    /// <summary>
    /// One step. <paramref name="waiting"/> holds the waiting replays' cards: their heroes count
    /// as seen, and the fetched replays among them count toward <c>MaxWaiting</c>.
    /// </summary>
    public async Task<TargetedStep> StepAsync(
        IReadOnlyList<FocusCard> waiting,
        CancellationToken cancellationToken
    )
    {
        FocusTargetedSettings targeted = settings.Targeted ?? new FocusTargetedSettings();
        if (!settings.Enabled || !targeted.Enabled)
        {
            return TargetedStep.Off;
        }

        DateTime now = clock();
        FocusTargetedState state = store.Read();
        state.FirstStepAtUtc ??= now;
        var waitingIds = new HashSet<int>();
        foreach (FocusCard card in waiting ?? Array.Empty<FocusCard>())
        {
            if (card?.ReplayId is int id)
            {
                waitingIds.Add(id);
            }

            foreach (FocusCardPlayer player in card?.Players ?? new List<FocusCardPlayer>())
            {
                if (!player.IsAi && !string.IsNullOrWhiteSpace(player.AttributeId))
                {
                    state.Seen[player.AttributeId] = now;
                }
            }
        }

        TargetedStep gate = Gate(state, targeted, waitingIds, now);
        if (gate != TargetedStep.Fetched)
        {
            store.Write(state);
            return gate;
        }

        Hero target = Target(state, targeted, now);
        if (target == null)
        {
            store.Write(state);
            return TargetedStep.NoTarget;
        }

        state.LastFetchAtUtc = now;
        state.Tried[target.AttributeId] = now;
        store.Write(state);
        try
        {
            return await FetchAsync(target, state, targeted, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            store.Write(state);
        }
    }

    private TargetedStep Gate(
        FocusTargetedState state,
        FocusTargetedSettings targeted,
        HashSet<int> waitingIds,
        DateTime now
    )
    {
        if (state.LastFetchAtUtc is DateTime last && now - last < targeted.Interval)
        {
            return TargetedStep.NotDue;
        }

        int fetchedWaiting = state.Fetched.Count(fetched => waitingIds.Contains(fetched.ReplayId));
        if (fetchedWaiting >= Math.Max(1, targeted.MaxWaiting))
        {
            return TargetedStep.EnoughWaiting;
        }

        if (state.CallsSince(now.AddDays(-7)) >= targeted.WeeklyCallBudget)
        {
            if (!budgetWarned)
            {
                budgetWarned = true;
                logger.LogWarning(
                    "Focus targeted fetches used their {Budget} Heroes Profile calls this week. The next waits for the oldest call to turn a week old.",
                    targeted.WeeklyCallBudget
                );
            }

            return TargetedStep.OverBudget;
        }

        budgetWarned = false;
        return TargetedStep.Fetched;
    }

    /// <summary>
    /// The hero to fetch for: in a catalog role, not focused for <c>After</c>, not on a waiting
    /// replay for <c>After</c>, and not tried for <c>RetryAfter</c>. The role with the fewest
    /// recent focus picks goes first, then a hero never focused, then the longest since its last
    /// focus. Null until the pool has had <c>After</c> to show each hero.
    /// </summary>
    internal Hero Target(FocusTargetedState state, FocusTargetedSettings targeted, DateTime now)
    {
        if (state.FirstStepAtUtc is DateTime first && now - first < targeted.After)
        {
            return null;
        }

        IReadOnlyList<FocusLedgerEntry> history = ledger.Read().History;
        Dictionary<string, DateTime> lastFocus = history
            .Where(entry => !string.IsNullOrWhiteSpace(entry.AttributeId))
            .GroupBy(entry => entry.AttributeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Max(entry => entry.PickedAtUtc),
                StringComparer.OrdinalIgnoreCase
            );
        List<string> recentRoles = history
            .TakeLast(Math.Max(1, settings.RecentWindow))
            .Select(entry => entry.Role)
            .ToList();

        return (roster() ?? Array.Empty<Hero>())
            .Where(hero =>
                hero != null
                && !string.IsNullOrWhiteSpace(hero.AttributeId)
                && Roles.Contains(hero.Role, StringComparer.OrdinalIgnoreCase)
                && !Within(lastFocus, hero.AttributeId, now, targeted.After)
                && !Within(state.Seen, hero.AttributeId, now, targeted.After)
                && !Within(state.Tried, hero.AttributeId, now, targeted.RetryAfter)
            )
            .OrderBy(hero =>
                recentRoles.Count(role =>
                    string.Equals(role, hero.Role, StringComparison.OrdinalIgnoreCase)
                )
            )
            .ThenBy(hero => lastFocus.ContainsKey(hero.AttributeId) ? 1 : 0)
            .ThenBy(hero =>
                lastFocus.TryGetValue(hero.AttributeId, out DateTime at) ? at : DateTime.MinValue
            )
            .ThenBy(hero => hero.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private async Task<TargetedStep> FetchAsync(
        Hero target,
        FocusTargetedState state,
        FocusTargetedSettings targeted,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<LeaderboardPlayer> players = await StrongPlayersAsync(
                target,
                state,
                targeted,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (players.Count == 0)
        {
            logger.LogInformation(
                "Focus target {Hero} ({Role}): no player on its last {Seasons} seasons' leaderboards has {Games} games and {WinRate:P0}. It is tried again after {RetryAfter}.",
                target.Name,
                target.Role,
                targeted.Seasons,
                settings.MinHeroGames,
                settings.MinWinRate,
                targeted.RetryAfter
            );
            return TargetedStep.NoStrongPlayer;
        }

        string since = clock()
            .Subtract(targeted.Lookback)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (LeaderboardPlayer player in players.Take(Math.Max(1, targeted.PlayersPerHero)))
        {
            HeroesProfileGlobalAnswer answer = await CallAsync(
                    state,
                    PlayerMatches.Path,
                    new Dictionary<string, string>
                    {
                        ["battletag"] = player.BattleTag,
                        ["region"] = player.Region.ToString(CultureInfo.InvariantCulture),
                        ["game_type"] = PlayerHeroStatsFetch.StormLeague,
                        ["hero"] = target.Name,
                        ["start_date"] = since,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (answer?.StatusCode != 200)
            {
                continue;
            }

            foreach (PlayerMatch match in PlayerMatches.Best(PlayerMatches.Parse(answer.Body)))
            {
                TargetedDownload result = await download(match.ReplayId, cancellationToken)
                    .ConfigureAwait(false);
                if (result != TargetedDownload.Downloaded)
                {
                    logger.LogDebug(
                        "Focus target {Hero}: replay {ReplayId} was not fetched ({Result}).",
                        target.Name,
                        match.ReplayId,
                        result
                    );
                    continue;
                }

                state.NoteFetched(
                    new FocusTargetedReplay
                    {
                        ReplayId = match.ReplayId,
                        Hero = target.Name,
                        AttributeId = target.AttributeId,
                        FetchedAtUtc = clock(),
                    }
                );
                logger.LogInformation(
                    "Focus target {Hero} ({Role}): fetched replay {ReplayId}, a {Result} game from {PlayedAt:yyyy-MM-dd} by a player with {Games} leaderboard games at {WinRate:P0} on the hero.",
                    target.Name,
                    target.Role,
                    match.ReplayId,
                    match.Won ? "won" : "lost",
                    match.PlayedAtUtc,
                    player.Games,
                    player.WinRate
                );
                return TargetedStep.Fetched;
            }
        }

        logger.LogInformation(
            "Focus target {Hero} ({Role}): none of {Players} strong players has a game on it since {Since} that spectate can play. It is tried again after {RetryAfter}.",
            target.Name,
            target.Role,
            Math.Min(players.Count, Math.Max(1, targeted.PlayersPerHero)),
            since,
            targeted.RetryAfter
        );
        return TargetedStep.NoGame;
    }

    /// <summary>The hero's leaderboard players over the seasons read, strong enough to focus.</summary>
    private async Task<IReadOnlyList<LeaderboardPlayer>> StrongPlayersAsync(
        Hero target,
        FocusTargetedState state,
        FocusTargetedSettings targeted,
        CancellationToken cancellationToken
    )
    {
        HeroesProfileGlobalAnswer patches = await CallAsync(
                state,
                "patches",
                new Dictionary<string, string>(),
                cancellationToken
            )
            .ConfigureAwait(false);
        int? current =
            patches?.StatusCode == 200 ? HeroLeaderboard.CurrentSeason(patches.Body) : null;
        var seasons = new List<IReadOnlyList<LeaderboardPlayer>>();
        int count = Math.Max(1, targeted.Seasons);
        for (int index = 0; index < count; index++)
        {
            var query = new Dictionary<string, string>
            {
                ["type"] = "hero",
                ["hero"] = target.Name,
                ["game_type"] = PlayerHeroStatsFetch.StormLeague,
            };
            if (current is int season)
            {
                query["season"] = (season - index).ToString(CultureInfo.InvariantCulture);
            }
            else if (index > 0)
            {
                break;
            }

            HeroesProfileGlobalAnswer answer = await CallAsync(
                    state,
                    HeroLeaderboard.Path,
                    query,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (answer?.StatusCode == 200)
            {
                seasons.Add(HeroLeaderboard.Parse(answer.Body));
            }
        }

        return HeroLeaderboard
            .Merge(seasons)
            .Where(player =>
                player.Games >= settings.MinHeroGames && player.WinRate >= settings.MinWinRate
            )
            .ToList();
    }

    /// <summary>One counted call; null when the budget ran out or Heroes Profile failed.</summary>
    private async Task<HeroesProfileGlobalAnswer> CallAsync(
        FocusTargetedState state,
        string path,
        IReadOnlyDictionary<string, string> query,
        CancellationToken cancellationToken
    )
    {
        FocusTargetedSettings targeted = settings.Targeted ?? new FocusTargetedSettings();
        DateTime now = clock();
        if (state.CallsSince(now.AddDays(-7)) >= targeted.WeeklyCallBudget)
        {
            return null;
        }

        state.NoteCall(now);
        try
        {
            return await caller
                .GetAsync(path, query.ToList(), jobTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Focus targeted fetch: Heroes Profile {Path} failed ({Failure}).",
                path,
                e.Message
            );
            return null;
        }
    }

    private static bool Within(
        IReadOnlyDictionary<string, DateTime> times,
        string key,
        DateTime now,
        TimeSpan span
    ) => times.TryGetValue(key, out DateTime at) && now - at < span;
}
