using HeroesReplay.Service.Spectator.Core.Extensions;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    using Polly;
    using Polly.Caching;
    using HeroesReplay.Core.Configuration;
    using HeroesReplay.Core.Models;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Options;

    public class HeroesProfileService : IHeroesProfileService
    {
        private readonly ILogger<HeroesProfileService> logger;
        private readonly CancellationTokenSource cts;
        private readonly HeroesProfileApiOptions apiOptions;
        private readonly SpectateOptions spectateOptions;
        private readonly IAsyncCacheProvider cacheProvider;
        private readonly IAsyncPolicy<IEnumerable<HeroesProfileReplay>> replaysByFilterCachePolicy;
        private readonly IAsyncPolicy<HeroesProfileReplay> replayCachePolicy;
        private readonly IAsyncPolicy<int> maxReplayIdCachePolicy;
        private readonly HttpClient httpClient;

        public HeroesProfileService(
            ILogger<HeroesProfileService> logger, 
            HttpClient httpClient, 
            IAsyncCacheProvider cacheProvider, 
            CancellationTokenSource cts, 
            IOptions<HeroesProfileApiOptions> apiOptions,
            IOptions<SpectateOptions> spectateOptions)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.cacheProvider = cacheProvider ?? throw new ArgumentNullException(nameof(cacheProvider));
            this.cts = cts ?? throw new ArgumentNullException(nameof(cts));
            this.apiOptions = apiOptions.Value;
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.httpClient.BaseAddress = this.apiOptions.BaseUri;

            replayCachePolicy = Policy.CacheAsync(
                cacheProvider: this.cacheProvider.AsyncFor<HeroesProfileReplay>(),
                ttlStrategy: new ResultTtl<HeroesProfileReplay>((context, replay) => new Ttl(TimeSpan.FromHours(2))),
                onCacheGet: OnCacheGet,
                onCachePut: OnCachePut,
                onCacheMiss: OnCacheMiss,
                onCacheGetError: OnCacheGetError,
                onCachePutError: OnCachePutError);

            replaysByFilterCachePolicy = Policy.CacheAsync(
                cacheProvider: this.cacheProvider.AsyncFor<IEnumerable<HeroesProfileReplay>>(),
                ttlStrategy: new ResultTtl<IEnumerable<HeroesProfileReplay>>((context, replays) => new Ttl(replays.Any() ? TimeSpan.FromHours(1) : TimeSpan.Zero)),
                onCacheGet: OnCacheGet,
                onCachePut: OnCachePut,
                onCacheMiss: OnCacheMiss,
                onCacheGetError: OnCacheGetError,
                onCachePutError: OnCachePutError);

            maxReplayIdCachePolicy = Policy.CacheAsync(
                cacheProvider: this.cacheProvider.AsyncFor<int>(),
                ttlStrategy: new ResultTtl<int>((context, replay) => new Ttl(TimeSpan.FromHours(1))),
                onCacheGet: OnCacheGet,
                onCachePut: OnCachePut,
                onCacheMiss: OnCacheMiss,
                onCacheGetError: OnCacheGetError,
                onCachePutError: OnCachePutError);
        }

        private void OnCacheGet(Context context, string key)
        {
            logger.LogInformation($"Cache Get for: {key}");
        }

        private void OnCachePut(Context context, string key)
        {
            logger.LogInformation($"Cache Put for: {key}");
        }

        private void OnCacheMiss(Context context, string key)
        {
            logger.LogInformation($"Cache Miss for: {key}");
        }
        private void OnCacheGetError(Context context, string key, Exception e)
        {
            logger.LogError(e, $"Cache Get error for: {key}");
        }

        private void OnCachePutError(Context context, string key, Exception e)
        {
            logger.LogError(e, $"Cache Put error for: {key}");
        }

        public async Task<HeroesProfileReplay> GetReplayByIdAsync(int replayId)
        {
            return await replayCachePolicy.ExecuteAsync(async (context, token) =>
            {
                HttpResponseMessage response = await Policy
                           .Handle<Exception>()
                           .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                           .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                           .ExecuteAsync(
                                action: (Context context, CancellationToken token) => httpClient.GetAsync(new Uri($"Replay/Min_id?min_id={replayId}&api_token={apiOptions.ApiKey}", UriKind.Relative), token),
                                context: new Context(),
                                cancellationToken: cts.Token)
                           .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    IEnumerable<HeroesProfileReplay> replays = await response.Content.ReadFromJsonAsync<IEnumerable<HeroesProfileReplay>>(cancellationToken: token);
                    return replays.FirstOrDefault(replays => replays.Id == replayId);
                }

                return null;
            },
            new Context(operationKey: $"{replayId}"), cts.Token);
        }

        public async Task<int> GetMaxReplayIdAsync()
        {
            try
            {
                return await maxReplayIdCachePolicy.ExecuteAsync(async (context, token) =>
                {
                    HttpResponseMessage response = await Policy
                            .Handle<Exception>()
                            .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                            .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                            .ExecuteAsync(
                                action: (Context context, CancellationToken token) => httpClient.GetAsync(new Uri($"Replay/Max?api_token={apiOptions.ApiKey}", UriKind.Relative), token),
                                context: context,
                                cancellationToken: token);

                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();

                        if (!string.IsNullOrWhiteSpace(content) && int.TryParse(content, out int maxId))
                        {
                            return maxId;
                        }
                    }

                    return apiOptions.FallbackMaxReplayId;

                }, new Context(operationKey: "MaxReplayId"), cts.Token);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not get max replayId from HeroesProfile API.");
            }

            return apiOptions.FallbackMaxReplayId;
        }

        public async Task<IEnumerable<HeroesProfileReplay>> GetReplaysByFilters(GameType? gameType = null, GameRank? gameRank = null, string gameMap = null)
        {
            try
            {
                var dictionary = new Dictionary<string, string>();
                if (gameType != null) dictionary.Add("game_type", gameType.Value.GetQueryValue());
                if (gameRank != null) dictionary.Add("rank", gameRank.Value.GetQueryValue());
                if (gameMap != null) dictionary.Add("game_map", gameMap);

                string filter;

                using (FormUrlEncodedContent content = new FormUrlEncodedContent(dictionary))
                {
                    filter = await content.ReadAsStringAsync();
                }

                return await replaysByFilterCachePolicy.ExecuteAsync(async (context, token) =>
                {
                    int maxId = await GetMaxReplayIdAsync();
                    context["minId"] = maxId - apiOptions.ApiMaxReturnedReplays;

                    // If nothing is found with the filter, try going back further

                    IEnumerable<HeroesProfileReplay> replays = await Policy
                               .Handle<Exception>()
                               .OrResult<IEnumerable<HeroesProfileReplay>>(replays => !replays.Any())
                               .WaitAndRetryAsync(retryCount: 20, sleepDurationProvider: (int retry, Context context) => TimeSpan.FromSeconds(1), onRetry: OnFilterRetry)
                               .ExecuteAsync(async (Context context, CancellationToken token) =>
                               {
                                   HttpResponseMessage response = await Policy
                                        .Handle<Exception>()
                                        .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                                        .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, OnRetry)
                                        .ExecuteAsync((context, token) => httpClient.GetAsync(new Uri($"Replay/Min_id?min_id={context["minId"]}&{context.OperationKey}&api_token={apiOptions.ApiKey}", UriKind.Relative), token), context, token);

                                   if (response.IsSuccessStatusCode)
                                   {
                                       IEnumerable<HeroesProfileReplay> replays = await response.Content.ReadFromJsonAsync<IEnumerable<HeroesProfileReplay>>(cancellationToken: token);

                                       var supported = replays
                                           .Where(x => x.Deleted == null)
                                           .Where(x => x.Url.Host.Contains(apiOptions.S3Bucket))
                                           .Where(x => spectateOptions.VersionsSupported.Contains(x.GameVersion));

                                       return supported;
                                   }
                                   else
                                   {
                                       return Enumerable.Empty<HeroesProfileReplay>();
                                   }

                               }, context, token)
                               .ConfigureAwait(false);

                    return replays;

                }, new Context(operationKey: filter), cts.Token);
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
                HttpResponseMessage response = await Policy
                           .Handle<Exception>()
                           .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                           .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                           .ExecuteAsync((context, token) => httpClient.GetAsync(new Uri($"Replay/Min_id?min_id={minId}&api_token={apiOptions.ApiKey}", UriKind.Relative), token), new Context(), cts.Token)
                           .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    IEnumerable<HeroesProfileReplay> replays = await response.Content.ReadFromJsonAsync<IEnumerable<HeroesProfileReplay>>();

                    return replays.Where(x => x.Deleted == null)
                        .Where(x => x.Url.Host.Contains(apiOptions.S3Bucket))
                        .Where(x => spectateOptions.VersionsSupported.Contains(x.GameVersion));
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not get replays from HeroesProfile Replays.");
            }

            return Enumerable.Empty<HeroesProfileReplay>();
        }

        private TimeSpan GetSleepDuration(int retry, Context context)
        {
            if (context.ContainsKey("retry-after"))
            {
                var retryAfter = (TimeSpan)context["retry-after"];
                logger.LogInformation($"getting sleep duration for retry attempt {retry}: {retryAfter}");
                return retryAfter;
            }

            return TimeSpan.FromSeconds(1);
        }

        private void OnRetry(DelegateResult<HttpResponseMessage> wrappedResponse, TimeSpan timeSpan, int retryAttempt, Context context)
        {
            if (wrappedResponse.Exception != null)
            {
                logger.LogError(wrappedResponse.Exception, "Error with Heroes Profile Service");
            }

            if (wrappedResponse.Result != null)
            {
                logger.LogDebug($"retry attempt {retryAttempt}: {wrappedResponse.Result.StatusCode}: {wrappedResponse.Result.ReasonPhrase}");
            }

            if (wrappedResponse?.Result?.Headers?.RetryAfter != null)
            {
                TimeSpan retryAfter = wrappedResponse.Result.Headers.RetryAfter.Delta.Value;
                logger.LogWarning($"Setting retry-after to {retryAfter}");
                context["retry-after"] = retryAfter;
            }
        }

        private void OnFilterRetry(DelegateResult<IEnumerable<HeroesProfileReplay>> wrappedResponse, TimeSpan timeSpan, int retryAttempt, Context context)
        {
            logger.LogWarning($"No results found for {context.OperationKey}. MinReplayId being lowered.");
            context["minId"] = (int)context["minId"] - apiOptions.ApiMaxReturnedReplays;
        }
    }
}