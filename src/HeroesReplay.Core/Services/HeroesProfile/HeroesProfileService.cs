using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;

using Polly;
using Polly.Caching;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Extensions;
using HeroesReplay.Core.Services.Shared;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class HeroesProfileService : IHeroesProfileService
    {
        private readonly ILogger<HeroesProfileService> logger;
        private readonly ProcessCancellationTokenProvider tokenProvider;
        private readonly AppSettings settings;
        private readonly IAsyncCacheProvider cacheProvider;
        private readonly IAsyncPolicy<IEnumerable<HeroesProfileReplay>> replaysByFilterCachePolicy;
        private readonly IAsyncPolicy<HeroesProfileReplay> replayCachePolicy;
        private readonly IAsyncPolicy<int> maxReplayIdCachePolicy;
        private readonly HttpClient httpClient;

        public HeroesProfileService(ILogger<HeroesProfileService> logger, HttpClient httpClient, IAsyncCacheProvider cacheProvider, ProcessCancellationTokenProvider tokenProvider, AppSettings settings)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.cacheProvider = cacheProvider ?? throw new ArgumentNullException(nameof(cacheProvider));
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.httpClient.BaseAddress = settings.HeroesProfileApi.BaseUri;

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
                ttlStrategy: new ResultTtl<int>((context, replay) => new Ttl(TimeSpan.FromHours(3))),
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
                           .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: this.GetSleepDuration, onRetry: this.OnRetry)
                           .ExecuteAsync(
                                action: (Context context, CancellationToken token) => httpClient.GetAsync(new Uri($"Replay/Min_id?min_id={replayId}&api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative), token),
                                context: new Context(),
                                cancellationToken: tokenProvider.Token)
                           .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    IEnumerable<HeroesProfileReplay> replays = await response.Content.ReadFromJsonAsync<IEnumerable<HeroesProfileReplay>>();
                    return replays.FirstOrDefault(replays => replays.Id == replayId);
                }

                return null;
            },
            new Context(operationKey: $"{replayId}"), tokenProvider.Token);
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
                            .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: this.GetSleepDuration, onRetry: this.OnRetry)
                            .ExecuteAsync(
                                action: (Context context, CancellationToken token) => httpClient.GetAsync(new Uri($"Replay/Max?api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative), token),
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

                    return settings.HeroesProfileApi.FallbackMaxReplayId;

                }, new Context(operationKey: "MaxReplayId"), tokenProvider.Token);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not get max replayId from HeroesProfile API.");
            }

            return settings.HeroesProfileApi.FallbackMaxReplayId;
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

                using (var content = new FormUrlEncodedContent(dictionary))
                {
                    filter = await content.ReadAsStringAsync();
                }

                return await replaysByFilterCachePolicy.ExecuteAsync(async (context, token) =>
                {
                    int maxId = await GetMaxReplayIdAsync();
                    context["minId"] = maxId - settings.HeroesProfileApi.ApiMaxReturnedReplays;

                    // If nothing is found with the filter, try going back further

                    IEnumerable<HeroesProfileReplay> replays = await Policy
                               .Handle<Exception>()
                               .OrResult<IEnumerable<HeroesProfileReplay>>(replays => !replays.Any())
                               .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnFilterRetry)
                               .ExecuteAsync(async (Context context, CancellationToken token) =>
                               {
                                   HttpResponseMessage response = await Policy
                                        .Handle<Exception>()
                                        .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                                        .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, OnRetry)
                                        .ExecuteAsync((context, token) => httpClient.GetAsync(new Uri($"Replay/Min_id?min_id={context["minId"]}&{context.OperationKey}&api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative)), context, token);

                                   if (response.IsSuccessStatusCode)
                                   {
                                       IEnumerable<HeroesProfileReplay> replays = await response.Content.ReadFromJsonAsync<IEnumerable<HeroesProfileReplay>>();

                                       return replays
                                           .Where(x => x.Deleted == null)
                                           .Where(x => x.Url.Host.Contains(settings.HeroesProfileApi.S3Bucket))
                                           .Where(x => settings.Spectate.VersionSupported.Equals(x.GameVersion));
                                   }
                                   else
                                   {
                                       return Enumerable.Empty<HeroesProfileReplay>();
                                   }

                               }, context, token)
                               .ConfigureAwait(false);

                    return Enumerable.Empty<HeroesProfileReplay>();

                }, new Context(operationKey: filter), tokenProvider.Token);
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
                           .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: this.GetSleepDuration, onRetry: this.OnRetry)
                           .ExecuteAsync((context, token) => httpClient.GetAsync(new Uri($"Replay/Min_id?min_id={minId}&api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative), token), new Context(), tokenProvider.Token)
                           .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    IEnumerable<HeroesProfileReplay> replays = await response.Content.ReadFromJsonAsync<IEnumerable<HeroesProfileReplay>>();

                    return replays.Where(x => x.Deleted == null)
                        .Where(x => x.Url.Host.Contains(settings.HeroesProfileApi.S3Bucket))
                        .Where(x => settings.Spectate.VersionSupported.Equals(x.GameVersion));
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

            context["minId"] = (int)context["minId"] - settings.HeroesProfileApi.ApiMaxReturnedReplays;
        }
    }
}