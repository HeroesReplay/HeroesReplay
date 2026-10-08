using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Telemetry;
using HeroesReplay.HeroesProfile.Client;
using HeroesReplay.HeroesProfile.Client.Replays;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;
using Polly;

namespace HeroesReplay.Core.HeroesProfile;

public class HeroesProfileService : IHeroesProfileService
{
    private static readonly ResiliencePropertyKey<int> FilterMinIdKey = new("minId");

    private readonly ILogger<HeroesProfileService> logger;
    private readonly CancellationTokenProvider tokenProvider;
    private readonly AppSettings settings;
    private readonly IMemoryCache cache;
    private readonly HeroesProfileClient kiotaClient;

    public HeroesProfileService(
        ILogger<HeroesProfileService> logger,
        IMemoryCache cache,
        CancellationTokenProvider tokenProvider,
        AppSettings settings,
        HeroesProfileClient kiotaClient
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        this.tokenProvider =
            tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.kiotaClient = kiotaClient ?? throw new ArgumentNullException(nameof(kiotaClient));
    }

    public async Task<HeroesProfileReplay> GetReplayByIdAsync(int replayId)
    {
        using Activity activity = HeroesReplayTelemetry.ActivitySource.StartActivity(
            "heroesreplay.heroesprofile.get_by_id"
        );
        activity?.SetTag("replay.id", replayId);
        return await MemoryCacheLookup
            .GetOrCreateAsync(
                cache,
                logger,
                $"hp-replay:{replayId}",
                _ => TimeSpan.FromHours(2),
                async token =>
                {
                    ReplaysGetResponse page = await GetReplaysPageAsync(
                        replayId - 1,
                        gameType: null,
                        gameMap: null,
                        token
                    );
                    return HeroesProfileReplayMapper
                        .ToReplays(page)
                        .FirstOrDefault(r => r.Id == replayId);
                },
                tokenProvider.Token
            )
            .ConfigureAwait(false);
    }

    public async Task<int> GetMaxReplayIdAsync()
    {
        try
        {
            return await MemoryCacheLookup
                .GetOrCreateAsync(
                    cache,
                    logger,
                    "hp-max-replay-id",
                    _ => TimeSpan.FromHours(1),
                    async token =>
                    {
                        ReplaysGetResponse page = await GetReplaysPageAsync(
                            settings.HeroesProfileApi.MinReplayId > 0
                                ? settings.HeroesProfileApi.MinReplayId
                                : null,
                            settings.HeroesProfileApi.GameTypes?.FirstOrDefault(),
                            gameMap: null,
                            token
                        );
                        if (page != null && page.MaxReplayId.GetValueOrDefault() > 0)
                        {
                            return page.MaxReplayId.Value;
                        }

                        return settings.HeroesProfileApi.FallbackMaxReplayId;
                    },
                    tokenProvider.Token
                )
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not get max replayId from HeroesProfile API.");
        }

        return settings.HeroesProfileApi.FallbackMaxReplayId;
    }

    public async Task<IEnumerable<HeroesProfileReplay>> GetReplaysByFilters(
        GameType? gameType = null,
        GameRank? gameRank = null,
        string gameMap = null
    )
    {
        try
        {
            var dictionary = new Dictionary<string, string>();
            if (gameType != null)
                dictionary.Add("game_type", gameType.Value.GetQueryValue());
            if (gameRank != null)
                dictionary.Add("rank", gameRank.Value.GetQueryValue());
            if (gameMap != null)
                dictionary.Add("game_map", gameMap);

            string filter;

            using (FormUrlEncodedContent content = new FormUrlEncodedContent(dictionary))
            {
                filter = await content.ReadAsStringAsync();
            }

            return await MemoryCacheLookup
                .GetOrCreateAsync(
                    cache,
                    logger,
                    $"hp-filter:{filter}",
                    replays => replays.Any() ? TimeSpan.FromHours(1) : TimeSpan.Zero,
                    token => LoadFilteredReplaysAsync(gameType, gameMap, filter, token),
                    tokenProvider.Token
                )
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not get replays from HeroesProfile Replays.");
        }

        return Enumerable.Empty<HeroesProfileReplay>();
    }

    public async Task<IEnumerable<HeroesProfileReplay>> GetReplaysByMinId(int minId)
    {
        ReplayListing page = await ListPageAsync(minId).ConfigureAwait(false);
        return page.Playable;
    }

    public async Task<ReplayListing> ListPageAsync(int minId)
    {
        try
        {
            ReplaysGetResponse page = await GetReplaysPageAsync(
                    minId,
                    settings.HeroesProfileApi.GameTypes?.FirstOrDefault(),
                    gameMap: null,
                    tokenProvider.Token
                )
                .ConfigureAwait(false);
            var rows = HeroesProfileReplayMapper.ToReplays(page).ToList();
            int highest = rows.Count == 0 ? 0 : rows.Max(replay => replay.Id);
            var playable = FilterListed(rows).Where(replay => replay.Id > minId).ToList();
            return new ReplayListing(playable, rows.Count > 0, highest, page?.NextAfter);
        }
        // A refused key is not transient. The caller pauses the listing and logs it once per
        // change (ReplayListBackoff, #358), so this is not an error line per call.
        catch (ApiException e) when (e.ResponseStatusCode is 401 or 403)
        {
            logger.LogDebug(
                "Heroes Profile refused the replay list after {MinReplayId} (HTTP {Status}).",
                minId,
                e.ResponseStatusCode
            );
            return ReplayListing.Rejected(e.ResponseStatusCode);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not get replays from HeroesProfile Replays.");
        }

        return ReplayListing.Unanswered;
    }

    public async Task<IReadOnlyList<HeroesProfileReplay>> ListAfterAsync(
        int after,
        CancellationToken cancellationToken
    )
    {
        ReplaysGetResponse page = await GetReplaysPageAsync(
                after > 0 ? after : 1,
                gameType: null,
                gameMap: null,
                cancellationToken
            )
            .ConfigureAwait(false);
        return HeroesProfileReplayMapper
            .ToReplays(page)
            .Where(replay => replay != null && replay.Id > after)
            .OrderBy(replay => replay.Id)
            .ToList();
    }

    /// <summary>
    /// An error answer throws <see cref="HeroesProfileApiException"/> with the status and the
    /// body's <c>error.code</c>, so a request can tell a deleted replay (403
    /// <c>replay_deleted</c>) from a key problem (#361).
    /// </summary>
    public Task DownloadReplayAsync(
        int replayId,
        Stream destination,
        CancellationToken cancellationToken
    ) => kiotaClient.DownloadReplayAsync(replayId, destination, cancellationToken);

    public async Task EnrichRankAsync(
        HeroesProfileReplay replay,
        CancellationToken cancellationToken
    )
    {
        if (replay == null)
        {
            return;
        }

        if (!HeroesProfileRankEnricher.ShouldLookup(replay))
        {
            string kept = HeroesProfileRankEnricher.Resolve(replay.GameType, replay.Rank, null);
            if (
                !string.IsNullOrWhiteSpace(replay.Rank)
                && string.IsNullOrWhiteSpace(kept)
                && !HeroesProfileRankEnricher.IsStormLeague(replay.GameType)
            )
            {
                logger.LogInformation(
                    "Replay {ReplayId} is not Storm League; rank badge hidden.",
                    replay.Id
                );
            }

            replay.Rank = kept;
            return;
        }

        try
        {
            double? mmr = replay.AverageMmr;
            try
            {
                var detail = await kiotaClient
                    .Replay[replay.Id]
                    .GetAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                double? fromPlayers = RankImage.AveragePlayerMmr(detail?.Players);
                if (fromPlayers.HasValue)
                {
                    mmr = fromPlayers;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    e,
                    "Could not load replay {ReplayId} to average player_mmr.",
                    replay.Id
                );
            }

            replay.AverageMmr = mmr ?? replay.AverageMmr;
            int? rounded = HeroesProfileRankEnricher.RoundMmr(mmr);
            if (!rounded.HasValue)
            {
                replay.Rank = null;
                logger.LogWarning(
                    "Replay {ReplayId} has no average player_mmr; rank badge hidden.",
                    replay.Id
                );
                return;
            }

            string tier = await MemoryCacheLookup
                .GetOrCreateAsync(
                    cache,
                    logger,
                    $"hp-sl-tier:{rounded.Value}",
                    value =>
                        string.IsNullOrWhiteSpace(value) ? TimeSpan.Zero : TimeSpan.FromHours(1),
                    token => LookupStormLeagueTierAsync(rounded.Value, token),
                    cancellationToken
                )
                .ConfigureAwait(false);

            replay.Rank = HeroesProfileRankEnricher.Resolve(replay.GameType, replay.Rank, tier);
            if (string.IsNullOrWhiteSpace(replay.Rank))
            {
                logger.LogWarning(
                    "Replay {ReplayId} Storm League tier lookup failed for MMR {Mmr}; rank badge hidden.",
                    replay.Id,
                    rounded.Value
                );
                return;
            }

            logger.LogInformation(
                "Replay {ReplayId} rank {Rank} (avg player_mmr {Mmr}).",
                replay.Id,
                replay.Rank,
                mmr
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            replay.Rank = HeroesProfileRankEnricher.Resolve(replay.GameType, replay.Rank, null);
            logger.LogWarning(e, "Could not fill rank for replay {ReplayId}.", replay.Id);
        }
    }

    private async Task<string> LookupStormLeagueTierAsync(
        int mmr,
        CancellationToken cancellationToken
    )
    {
        string tier = await kiotaClient
            .GetMmrTierAsync("Storm League", mmr, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(tier) || RankImage.SourceName(tier) == null)
        {
            return string.Empty;
        }

        return tier.Trim();
    }

    private async Task<IEnumerable<HeroesProfileReplay>> LoadFilteredReplaysAsync(
        GameType? gameType,
        string gameMap,
        string filter,
        CancellationToken token
    )
    {
        int maxId = await GetMaxReplayIdAsync().ConfigureAwait(false);
        ResiliencePipeline<IEnumerable<HeroesProfileReplay>> pipeline = ResilienceRetry.Constant<
            IEnumerable<HeroesProfileReplay>
        >(
            retries: 20,
            delay: TimeSpan.FromSeconds(1),
            retry: outcome =>
                ResilienceRetry.Failed(outcome, replays => replays == null || !replays.Any()),
            onRetry: args =>
            {
                logger.LogWarning(
                    "No results found for {Filter}. MinReplayId being lowered.",
                    filter
                );
                if (args.Context.Properties.TryGetValue(FilterMinIdKey, out int minId))
                {
                    args.Context.Properties.Set(
                        FilterMinIdKey,
                        minId - settings.HeroesProfileApi.ApiMaxReturnedReplays
                    );
                }
            }
        );

        ResilienceContext context = ResilienceContextPool.Shared.Get(token);
        try
        {
            context.Properties.Set(
                FilterMinIdKey,
                maxId - settings.HeroesProfileApi.ApiMaxReturnedReplays
            );
            return await pipeline
                .ExecuteAsync(
                    ctx => new ValueTask<IEnumerable<HeroesProfileReplay>>(ReadFilterPage(ctx)),
                    context
                )
                .ConfigureAwait(false);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }

        async Task<IEnumerable<HeroesProfileReplay>> ReadFilterPage(ResilienceContext ctx)
        {
            string typeQuery =
                gameType?.GetQueryValue() ?? settings.HeroesProfileApi.GameTypes?.FirstOrDefault();
            ctx.Properties.TryGetValue(FilterMinIdKey, out int after);
            ReplaysGetResponse page = await GetReplaysPageAsync(
                    after,
                    typeQuery,
                    gameMap,
                    ctx.CancellationToken
                )
                .ConfigureAwait(false);
            return FilterListed(HeroesProfileReplayMapper.ToReplays(page));
        }
    }

    private Task<ReplaysGetResponse> GetReplaysPageAsync(
        int? after,
        string gameType,
        string gameMap,
        CancellationToken token
    )
    {
        return kiotaClient.Replays.GetAsync(
            config =>
            {
                if (after.HasValue && after.Value > 0)
                {
                    config.QueryParameters.After = after;
                }

                if (!string.IsNullOrWhiteSpace(gameType))
                {
                    config.QueryParameters.GameType = gameType;
                }

                if (!string.IsNullOrWhiteSpace(gameMap))
                {
                    config.QueryParameters.GameMap = gameMap;
                }
            },
            token
        );
    }

    private IEnumerable<HeroesProfileReplay> FilterListed(IEnumerable<HeroesProfileReplay> replays)
    {
        if (replays == null)
        {
            return Enumerable.Empty<HeroesProfileReplay>();
        }

        return replays
            .Where(x => x.Deleted is not > 0)
            .Where(x => settings.HeroesProfileApi.IsAllowedGameType(x.GameType))
            .Where(x =>
                x.Downloadable == true
                || (x.Downloadable != false && settings.HeroesProfileApi.MatchesReplayUrl(x.Url))
            )
            .Where(x =>
                GameVersionOrder.Allows(
                    x.GameVersion,
                    settings.Spectate?.VersionsSupported,
                    settings.Spectate?.MinimumGameVersion
                )
            );
    }
}
