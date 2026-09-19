using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Shared;
using HeroesReplay.HeroesProfile.Client;
using HeroesReplay.HeroesProfile.Client.Replays;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions;
using Polly;
using Polly.Caching;
using PollyContext = Polly.Context;

namespace HeroesReplay.Core.Services.HeroesProfile;

public class HeroesProfileService : IHeroesProfileService
{
    private readonly ILogger<HeroesProfileService> logger;
    private readonly CancellationTokenProvider tokenProvider;
    private readonly AppSettings settings;
    private readonly IAsyncCacheProvider cacheProvider;
    private readonly IAsyncPolicy<IEnumerable<HeroesProfileReplay>> replaysByFilterCachePolicy;
    private readonly IAsyncPolicy<HeroesProfileReplay> replayCachePolicy;
    private readonly IAsyncPolicy<int> maxReplayIdCachePolicy;
    private readonly HttpClient httpClient;
    private readonly HeroesProfileClient kiotaClient;

    public HeroesProfileService(
        ILogger<HeroesProfileService> logger,
        HttpClient httpClient,
        IAsyncCacheProvider cacheProvider,
        CancellationTokenProvider tokenProvider,
        AppSettings settings,
        HeroesProfileClient kiotaClient
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.cacheProvider =
            cacheProvider ?? throw new ArgumentNullException(nameof(cacheProvider));
        this.tokenProvider =
            tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.kiotaClient = kiotaClient ?? throw new ArgumentNullException(nameof(kiotaClient));
        this.httpClient.BaseAddress = ReplayApiBase;

        replayCachePolicy = Policy.CacheAsync(
            cacheProvider: this.cacheProvider.AsyncFor<HeroesProfileReplay>(),
            ttlStrategy: new ResultTtl<HeroesProfileReplay>(
                (context, replay) => new Ttl(TimeSpan.FromHours(2))
            ),
            onCacheGet: OnCacheGet,
            onCachePut: OnCachePut,
            onCacheMiss: OnCacheMiss,
            onCacheGetError: OnCacheGetError,
            onCachePutError: OnCachePutError
        );

        replaysByFilterCachePolicy = Policy.CacheAsync(
            cacheProvider: this.cacheProvider.AsyncFor<IEnumerable<HeroesProfileReplay>>(),
            ttlStrategy: new ResultTtl<IEnumerable<HeroesProfileReplay>>(
                (context, replays) => new Ttl(replays.Any() ? TimeSpan.FromHours(1) : TimeSpan.Zero)
            ),
            onCacheGet: OnCacheGet,
            onCachePut: OnCachePut,
            onCacheMiss: OnCacheMiss,
            onCacheGetError: OnCacheGetError,
            onCachePutError: OnCachePutError
        );

        maxReplayIdCachePolicy = Policy.CacheAsync(
            cacheProvider: this.cacheProvider.AsyncFor<int>(),
            ttlStrategy: new ResultTtl<int>((context, replay) => new Ttl(TimeSpan.FromHours(1))),
            onCacheGet: OnCacheGet,
            onCachePut: OnCachePut,
            onCacheMiss: OnCacheMiss,
            onCacheGetError: OnCacheGetError,
            onCachePutError: OnCachePutError
        );
    }

    private void OnCacheGet(PollyContext context, string key)
    {
        logger.LogInformation($"Cache Get for: {key}");
    }

    private void OnCachePut(PollyContext context, string key)
    {
        logger.LogInformation($"Cache Put for: {key}");
    }

    private void OnCacheMiss(PollyContext context, string key)
    {
        logger.LogInformation($"Cache Miss for: {key}");
    }

    private void OnCacheGetError(PollyContext context, string key, Exception e)
    {
        logger.LogError(e, $"Cache Get error for: {key}");
    }

    private void OnCachePutError(PollyContext context, string key, Exception e)
    {
        logger.LogError(e, $"Cache Put error for: {key}");
    }

    public async Task<HeroesProfileReplay> GetReplayByIdAsync(int replayId)
    {
        using Activity activity = HeroesReplayTelemetry.ActivitySource.StartActivity(
            "heroesreplay.heroesprofile.get_by_id"
        );
        activity?.SetTag("replay.id", replayId);
        return await replayCachePolicy.ExecuteAsync(
            async (context, token) =>
            {
                if (UseKiota)
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
                }

                HttpResponseMessage response = await Policy
                    .Handle<Exception>()
                    .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                    .WaitAndRetryAsync(
                        retryCount: 10,
                        sleepDurationProvider: GetSleepDuration,
                        onRetry: OnRetry
                    )
                    .ExecuteAsync(
                        action: (PollyContext context, CancellationToken token) =>
                            httpClient.GetAsync(
                                new Uri(
                                    BuildListPath(replayId - 1, gameType: null),
                                    UriKind.Relative
                                ),
                                token
                            ),
                        context: new PollyContext(),
                        cancellationToken: tokenProvider.Token
                    )
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    IEnumerable<HeroesProfileReplay> replays = await ReadReplayListAsync(
                        response,
                        token
                    );
                    return replays.FirstOrDefault(r => r.Id == replayId);
                }

                return null;
            },
            new PollyContext(operationKey: $"{replayId}"),
            tokenProvider.Token
        );
    }

    public async Task<int> GetMaxReplayIdAsync()
    {
        try
        {
            return await maxReplayIdCachePolicy.ExecuteAsync(
                async (context, token) =>
                {
                    if (UseKiota)
                    {
                        ReplaysGetResponse page = await GetReplaysPageAsync(
                            after: null,
                            settings.HeroesProfileApi.GameTypes?.FirstOrDefault(),
                            gameMap: null,
                            token
                        );
                        if (page != null && page.MaxReplayId.GetValueOrDefault() > 0)
                        {
                            return page.MaxReplayId.Value;
                        }

                        return settings.HeroesProfileApi.FallbackMaxReplayId;
                    }

                    HttpResponseMessage response = await Policy
                        .Handle<Exception>()
                        .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                        .WaitAndRetryAsync(
                            retryCount: 10,
                            sleepDurationProvider: GetSleepDuration,
                            onRetry: OnRetry
                        )
                        .ExecuteAsync(
                            action: (PollyContext context, CancellationToken token) =>
                                httpClient.GetAsync(
                                    new Uri(BuildMaxPath(), UriKind.Relative),
                                    token
                                ),
                            context: context,
                            cancellationToken: token
                        );

                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync(token);
                        if (
                            !string.IsNullOrWhiteSpace(content)
                            && int.TryParse(content.Trim(), out int maxId)
                        )
                        {
                            return maxId;
                        }
                    }

                    return settings.HeroesProfileApi.FallbackMaxReplayId;
                },
                new PollyContext(operationKey: "MaxReplayId"),
                tokenProvider.Token
            );
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

            return await replaysByFilterCachePolicy.ExecuteAsync(
                async (context, token) =>
                {
                    int maxId = await GetMaxReplayIdAsync();
                    context["minId"] = maxId - settings.HeroesProfileApi.ApiMaxReturnedReplays;

                    // If nothing is found with the filter, try going back further

                    IEnumerable<HeroesProfileReplay> replays = await Policy
                        .Handle<Exception>()
                        .OrResult<IEnumerable<HeroesProfileReplay>>(replays => !replays.Any())
                        .WaitAndRetryAsync(
                            retryCount: 20,
                            sleepDurationProvider: (int retry, PollyContext context) =>
                                TimeSpan.FromSeconds(1),
                            onRetry: OnFilterRetry
                        )
                        .ExecuteAsync(
                            async (PollyContext context, CancellationToken token) =>
                            {
                                string typeQuery =
                                    gameType?.GetQueryValue()
                                    ?? settings.HeroesProfileApi.GameTypes?.FirstOrDefault();
                                int after = (int)context["minId"];
                                if (UseKiota)
                                {
                                    ReplaysGetResponse page = await GetReplaysPageAsync(
                                        after,
                                        typeQuery,
                                        gameMap,
                                        token
                                    );
                                    return FilterListed(HeroesProfileReplayMapper.ToReplays(page));
                                }

                                HttpResponseMessage response = await Policy
                                    .Handle<Exception>()
                                    .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                                    .WaitAndRetryAsync(
                                        retryCount: 10,
                                        sleepDurationProvider: GetSleepDuration,
                                        OnRetry
                                    )
                                    .ExecuteAsync(
                                        (context, token) =>
                                            httpClient.GetAsync(
                                                new Uri(
                                                    BuildListPath(after, typeQuery, gameMap),
                                                    UriKind.Relative
                                                ),
                                                token
                                            ),
                                        context,
                                        token
                                    );

                                if (response.IsSuccessStatusCode)
                                {
                                    return FilterListed(await ReadReplayListAsync(response, token));
                                }
                                else
                                {
                                    return Enumerable.Empty<HeroesProfileReplay>();
                                }
                            },
                            context,
                            token
                        )
                        .ConfigureAwait(false);

                    return replays;
                },
                new PollyContext(operationKey: filter),
                tokenProvider.Token
            );
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not get replays from HeroesProfile Replays.");
        }

        return Enumerable.Empty<HeroesProfileReplay>();
    }

    public async Task<IEnumerable<HeroesProfileReplay>> GetReplaysByMinId(int minId)
    {
        try
        {
            if (UseKiota)
            {
                ReplaysGetResponse page = await GetReplaysPageAsync(
                    minId,
                    settings.HeroesProfileApi.GameTypes?.FirstOrDefault(),
                    gameMap: null,
                    tokenProvider.Token
                );
                return FilterListed(HeroesProfileReplayMapper.ToReplays(page));
            }

            HttpResponseMessage response = await Policy
                .Handle<Exception>()
                .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                .WaitAndRetryAsync(
                    retryCount: 10,
                    sleepDurationProvider: GetSleepDuration,
                    onRetry: OnRetry
                )
                .ExecuteAsync(
                    (context, token) =>
                        httpClient.GetAsync(
                            new Uri(
                                BuildListPath(
                                    minId,
                                    settings.HeroesProfileApi.GameTypes?.FirstOrDefault()
                                ),
                                UriKind.Relative
                            ),
                            token
                        ),
                    new PollyContext(),
                    tokenProvider.Token
                )
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return FilterListed(await ReadReplayListAsync(response, tokenProvider.Token));
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not get replays from HeroesProfile Replays.");
        }

        return Enumerable.Empty<HeroesProfileReplay>();
    }

    public async Task DownloadReplayAsync(
        int replayId,
        Stream destination,
        CancellationToken cancellationToken
    )
    {
        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        if (UseKiota)
        {
            using Stream network = await kiotaClient
                .Download.Replay.GetAsync(
                    config => config.QueryParameters.ReplayID = replayId,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (network == null)
            {
                throw new InvalidOperationException(
                    $"Heroes Profile v1 download returned no content for replay {replayId}."
                );
            }

            await network.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            return;
        }

        using HttpClient downloadClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        Uri downloadUri = new Uri(
            settings.HeroesProfileApi.BaseUri,
            $"Replay/Download?replayID={replayId}&api_token={settings.HeroesProfileApi.ApiKey}"
        );
        using HttpResponseMessage response = await downloadClient
            .GetAsync(downloadUri, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream networkStream = await response.Content.ReadAsStreamAsync(
            cancellationToken
        );
        await networkStream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private bool UseKiota => settings.HeroesProfileApi.UseExternalV1;

    private Uri ReplayApiBase => settings.HeroesProfileApi.BaseUri;

    private async Task<ReplaysGetResponse> GetReplaysPageAsync(
        int? after,
        string gameType,
        string gameMap,
        CancellationToken token
    )
    {
        return await Policy
            .Handle<Exception>(ShouldRetryKiota)
            .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: _ => TimeSpan.FromSeconds(1))
            .ExecuteAsync(
                ct =>
                    kiotaClient.Replays.GetAsync(
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
                        ct
                    ),
                token
            )
            .ConfigureAwait(false);
    }

    private static bool ShouldRetryKiota(Exception exception)
    {
        if (exception is ApiException api)
        {
            int status = api.ResponseStatusCode;
            return status == 0 || status == 429 || status >= 500;
        }

        return true;
    }

    private string BuildMaxPath() => $"Replay/Max?api_token={settings.HeroesProfileApi.ApiKey}";

    private string BuildListPath(int? after, string gameType, string gameMap = null)
    {
        int minId = after.GetValueOrDefault();
        string path = $"Replay/Min_id?min_id={minId}&api_token={settings.HeroesProfileApi.ApiKey}";
        if (!string.IsNullOrWhiteSpace(gameType))
        {
            path += "&game_type=" + Uri.EscapeDataString(gameType);
        }

        if (!string.IsNullOrWhiteSpace(gameMap))
        {
            path += "&game_map=" + Uri.EscapeDataString(gameMap);
        }

        return path;
    }

    private static async Task<IEnumerable<HeroesProfileReplay>> ReadReplayListAsync(
        HttpResponseMessage response,
        CancellationToken token
    )
    {
        IEnumerable<HeroesProfileReplay> list = await response.Content.ReadFromJsonAsync<
            IEnumerable<HeroesProfileReplay>
        >(cancellationToken: token);
        return list ?? Enumerable.Empty<HeroesProfileReplay>();
    }

    private IEnumerable<HeroesProfileReplay> FilterListed(IEnumerable<HeroesProfileReplay> replays)
    {
        if (replays == null)
        {
            return Enumerable.Empty<HeroesProfileReplay>();
        }

        IEnumerable<string> versions = settings.Spectate?.VersionsSupported;
        return replays
            .Where(x => x.Deleted is not > 0)
            .Where(x => settings.HeroesProfileApi.IsAllowedGameType(x.GameType))
            .Where(x =>
                x.Downloadable == true
                || (x.Downloadable != false && settings.HeroesProfileApi.MatchesReplayUrl(x.Url))
            )
            .Where(x => versions == null || !versions.Any() || versions.Contains(x.GameVersion));
    }

    private TimeSpan GetSleepDuration(int retry, PollyContext context)
    {
        if (context.ContainsKey("retry-after"))
        {
            var retryAfter = (TimeSpan)context["retry-after"];
            logger.LogInformation(
                $"getting sleep duration for retry attempt {retry}: {retryAfter}"
            );
            return retryAfter;
        }

        return TimeSpan.FromSeconds(1);
    }

    private void OnRetry(
        DelegateResult<HttpResponseMessage> wrappedResponse,
        TimeSpan timeSpan,
        int retryAttempt,
        PollyContext context
    )
    {
        if (wrappedResponse.Exception != null)
        {
            logger.LogError(wrappedResponse.Exception, "Error with Heroes Profile Service");
        }

        if (wrappedResponse.Result != null)
        {
            logger.LogDebug(
                $"retry attempt {retryAttempt}: {wrappedResponse.Result.StatusCode}: {wrappedResponse.Result.ReasonPhrase}"
            );
        }

        if (wrappedResponse?.Result?.Headers?.RetryAfter != null)
        {
            TimeSpan retryAfter = wrappedResponse.Result.Headers.RetryAfter.Delta.Value;
            logger.LogWarning($"Setting retry-after to {retryAfter}");
            context["retry-after"] = retryAfter;
        }
    }

    private void OnFilterRetry(
        DelegateResult<IEnumerable<HeroesProfileReplay>> wrappedResponse,
        TimeSpan timeSpan,
        int retryAttempt,
        PollyContext context
    )
    {
        logger.LogWarning(
            $"No results found for {context.OperationKey}. MinReplayId being lowered."
        );
        context["minId"] = (int)context["minId"] - settings.HeroesProfileApi.ApiMaxReturnedReplays;
    }
}
