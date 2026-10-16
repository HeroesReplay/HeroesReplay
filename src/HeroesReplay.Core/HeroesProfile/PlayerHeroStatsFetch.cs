using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.HeroesProfile.Client;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// Looks up a player's Storm League record on one hero (#433):
/// <c>players/heroes?battletag=…&amp;region=…&amp;game_type=sl&amp;hero=…</c> through the shared
/// <see cref="HeroesProfileCaller"/>, cached in <see cref="PlayerHeroStatsStore"/>. A private
/// player (403 <c>player_unavailable</c>), a missing one (404) or a refused parameter (422) is
/// kept as such for <c>UnknownRetryAfter</c>; any other 401 or 403 stops the lookups until a
/// restart, with one warning; past <c>WeeklyCallBudget</c> calls in 7 days nothing is asked.
/// </summary>
public sealed class PlayerHeroStatsFetch
{
    public const string PlayersHeroesPath = "players/heroes";

    /// <summary>The Storm League game type, as the player endpoints take it.</summary>
    public const string StormLeague = "sl";

    private static readonly Regex BattleTagPattern = new(
        @"^[^\s#]+#\d{1,10}$",
        RegexOptions.CultureInvariant
    );

    private readonly HeroesProfileCaller caller;
    private readonly PlayerHeroStatsStore store;
    private readonly PlayerStatsSettings settings;
    private readonly ILogger logger;
    private readonly Func<DateTimeOffset> clock;

    public PlayerHeroStatsFetch(
        HeroesProfileCaller caller,
        PlayerHeroStatsStore store,
        PlayerStatsSettings settings,
        ILogger logger,
        Func<DateTimeOffset> clock = null
    )
    {
        this.caller = caller ?? throw new ArgumentNullException(nameof(caller));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.settings = settings ?? new PlayerStatsSettings();
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>True after a 401 or 403 about the key. Nothing more is asked by this instance.</summary>
    public bool Stopped { get; private set; }

    public PlayerHeroStatsStore Store => store;

    public static bool IsBattleTag(string battleTag) =>
        !string.IsNullOrWhiteSpace(battleTag) && BattleTagPattern.IsMatch(battleTag.Trim());

    /// <summary>NA, EU, KR and CN are 1, 2, 3 and 5; anything else (PTR) has no record.</summary>
    public static bool IsRegion(int region) => region is 1 or 2 or 3 or 5;

    public async Task<PlayerHeroLookup> LookupAsync(
        string battleTag,
        int region,
        string heroName,
        string attributeId,
        CancellationToken cancellationToken
    )
    {
        if (!IsBattleTag(battleTag) || !IsRegion(region) || string.IsNullOrWhiteSpace(heroName))
        {
            return new PlayerHeroLookup(
                PlayerHeroLookupStatus.Unavailable,
                Reason: "not a player and hero Heroes Profile can look up"
            );
        }

        string key = string.IsNullOrWhiteSpace(attributeId) ? heroName.Trim() : attributeId.Trim();
        DateTimeOffset now = clock();
        PlayerHeroLookup cached = store.Fresh(
            battleTag,
            region,
            key,
            now,
            settings.MaxAge,
            settings.UnknownRetryAfter
        );
        if (cached != null)
        {
            return cached;
        }

        if (Stopped)
        {
            return new PlayerHeroLookup(PlayerHeroLookupStatus.Refused, Reason: "stopped");
        }

        if (store.CallsThisWeek(now) >= Math.Max(0, settings.WeeklyCallBudget))
        {
            return new PlayerHeroLookup(PlayerHeroLookupStatus.OverBudget, Reason: "weekly budget");
        }

        store.NoteCall(now);
        HeroesProfileGlobalAnswer answer;
        try
        {
            answer = await caller
                .GetAsync(
                    PlayersHeroesPath,
                    Query(battleTag, region, heroName),
                    settings.JobTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (HeroStatsException e)
        {
            logger.LogInformation(
                "Heroes Profile had no record for a {Hero} player this time: {Reason}",
                heroName,
                e.Message
            );
            return new PlayerHeroLookup(PlayerHeroLookupStatus.Unavailable, Reason: e.Message);
        }

        PlayerHeroLookup lookup = Read(answer, heroName, attributeId);
        if (lookup.Status == PlayerHeroLookupStatus.Refused)
        {
            Stopped = true;
            logger.LogWarning(
                "Heroes Profile refused {Path} ({Status} {Code}). Player lookups stop until a restart; focus picks treat every record as unknown.",
                PlayersHeroesPath,
                answer.StatusCode,
                answer.ErrorCode
            );
            return lookup;
        }

        if (lookup.Status != PlayerHeroLookupStatus.Unavailable || answer.StatusCode == 422)
        {
            store.Write(battleTag, region, key, lookup, clock());
        }

        return lookup;
    }

    /// <summary>What one answer means for the player's record.</summary>
    public static PlayerHeroLookup Read(
        HeroesProfileGlobalAnswer answer,
        string heroName,
        string attributeId
    )
    {
        switch (answer?.StatusCode)
        {
            case 200:
                PlayerHeroStats stats = PlayerHeroStatsJson.Parse(
                    answer.Body,
                    attributeId,
                    heroName
                );
                return stats == null
                    ? new PlayerHeroLookup(PlayerHeroLookupStatus.NoGames)
                    : new PlayerHeroLookup(PlayerHeroLookupStatus.Known, stats);
            case 403
                when string.Equals(
                    answer.ErrorCode,
                    "player_unavailable",
                    StringComparison.Ordinal
                ):
                return new PlayerHeroLookup(
                    PlayerHeroLookupStatus.Private,
                    Reason: answer.ErrorCode
                );
            case 404:
                return new PlayerHeroLookup(
                    PlayerHeroLookupStatus.NotFound,
                    Reason: answer.ErrorCode
                );
            case 401:
            case 403:
                return new PlayerHeroLookup(
                    PlayerHeroLookupStatus.Refused,
                    Reason: answer.ErrorCode
                );
            default:
                return new PlayerHeroLookup(
                    PlayerHeroLookupStatus.Unavailable,
                    Reason: $"{answer?.StatusCode} {answer?.ErrorCode}".Trim()
                );
        }
    }

    public static IReadOnlyList<KeyValuePair<string, string>> Query(
        string battleTag,
        int region,
        string heroName
    ) =>
        new[]
        {
            new KeyValuePair<string, string>("battletag", battleTag.Trim()),
            new KeyValuePair<string, string>(
                "region",
                region.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ),
            new KeyValuePair<string, string>("game_type", StormLeague),
            new KeyValuePair<string, string>("hero", heroName.Trim()),
        };
}
