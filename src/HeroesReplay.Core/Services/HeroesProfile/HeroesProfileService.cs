using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using Polly;
using Polly.Caching;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;


namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class HeroesProfileService : IHeroesProfileService
    {
        public const string ApiClient = nameof(ApiClient);
        public const string ExtensionApiClient = nameof(ExtensionApiClient);

        private readonly ILogger<HeroesProfileService> logger;
        private readonly CancellationTokenProvider tokenProvider;
        private readonly AppSettings settings;

        private const string CreateReplayUrl = @"save/replay";
        private const string UpdateReplayUrl = @"update/replay/";
        private const string CreatePlayerUrl = @"save/player";
        private const string UpdatePlayerUrl = @"update/player";
        private const string SaveTalentUrl = @"save/talent";
        private const string NotifyUrl = @"notify/talent/update";

        private readonly FormUrlEncodedContent notifyContent;

        private readonly IAsyncCacheProvider cacheProvider;
        private readonly IAsyncPolicy<List<HeroesProfileReplay>> minIdCachePolicy;
        private readonly IAsyncPolicy<ReplayData> replayDataCachePolicy;
        private readonly IAsyncPolicy<string> tierCachePolicy;
        private readonly IAsyncPolicy<HeroesProfileReplay> replayCachePolicy;
        private readonly IAsyncPolicy<int> maxReplayIdCachePolicy;

        private readonly HttpClient extensionApiClient;
        private readonly HttpClient apiClient;

        public HeroesProfileService(ILogger<HeroesProfileService> logger, IHttpClientFactory httpClientFactory, IAsyncCacheProvider cacheProvider, CancellationTokenProvider tokenProvider, AppSettings settings)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.cacheProvider = cacheProvider ?? throw new ArgumentNullException(nameof(cacheProvider));
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.apiClient = httpClientFactory.CreateClient(ApiClient);
            this.extensionApiClient = httpClientFactory.CreateClient(ExtensionApiClient);

            notifyContent = new(new Dictionary<string, string>
            {
                { ExtensionFormKeys.TwitchKey, settings.TwitchExtension.ApiKey },
                { ExtensionFormKeys.Email, settings.TwitchExtension.ApiEmail },
                { ExtensionFormKeys.TwitchUserName, settings.TwitchExtension.TwitchUserName },
                { ExtensionFormKeys.UserId, settings.TwitchExtension.ApiUserId }
            });

            replayDataCachePolicy = Policy.CacheAsync(
                cacheProvider: this.cacheProvider.AsyncFor<ReplayData>(),
                ttlStrategy: new ResultTtl<ReplayData>((context, data) => new Ttl(TimeSpan.FromHours(6))),
                onCacheGet: OnCacheGet,
                onCachePut: OnCachePut,
                onCacheMiss: OnCacheMiss,
                onCacheGetError: OnCacheGetError,
                onCachePutError: OnCachePutError);

            tierCachePolicy = Policy.CacheAsync(
                cacheProvider: this.cacheProvider.AsyncFor<string>(),
                ttlStrategy: new ResultTtl<string>((context, data) => new Ttl(TimeSpan.FromHours(5))),
                onCacheGet: OnCacheGet,
                onCachePut: OnCachePut,
                onCacheMiss: OnCacheMiss,
                onCacheGetError: OnCacheGetError,
                onCachePutError: OnCachePutError);

            replayCachePolicy = Policy.CacheAsync(
                cacheProvider: this.cacheProvider.AsyncFor<HeroesProfileReplay>(),
                ttlStrategy: new ResultTtl<HeroesProfileReplay>((context, replay) => new Ttl(TimeSpan.FromHours(2))),
                onCacheGet: OnCacheGet,
                onCachePut: OnCachePut,
                onCacheMiss: OnCacheMiss,
                onCacheGetError: OnCacheGetError,
                onCachePutError: OnCachePutError);

            maxReplayIdCachePolicy = Policy.CacheAsync(
                cacheProvider: this.cacheProvider.AsyncFor<int>(),
                ttlStrategy: new ResultTtl<int>((context, replay) => new Ttl(TimeSpan.FromHours(12))),
                onCacheGet: OnCacheGet,
                onCachePut: OnCachePut,
                onCacheMiss: OnCacheMiss,
                onCacheGetError: OnCacheGetError,
                onCachePutError: OnCachePutError);

            minIdCachePolicy = Policy.CacheAsync(
                cacheProvider: this.cacheProvider.AsyncFor<List<HeroesProfileReplay>>(),
                ttlStrategy: new ResultTtl<List<HeroesProfileReplay>>((context, replay) => new Ttl(TimeSpan.FromHours(1))),
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

        public async Task<IEnumerable<HeroesProfileReplay>> ListReplaysAllAsync(int minId)
        {
            try
            {
                HttpResponseMessage response = await Policy
                           .Handle<Exception>()
                           .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                           .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                           .ExecuteAsync(async (context, token) => await apiClient.GetAsync(new Uri($"Replay/Min_id?min_id={minId}&api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative), token), new Context(), tokenProvider.Token)
                           .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Deserializing replays...");

                    return JsonSerializer.Deserialize<IEnumerable<HeroesProfileReplay>>(await response.Content.ReadAsStringAsync()).Where(x => x.Deleted == null);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not get replays from HeroesProfile Replays.");
            }

            return Enumerable.Empty<HeroesProfileReplay>();
        }

        public async Task<string> CreateReplaySessionAsync(ExtensionPayload payload, CancellationToken token)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            try
            {
                using (CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, tokenProvider.Token))
                {
                    HttpResponseMessage response = await Policy
                               .Handle<Exception>()
                               .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                               .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                               .ExecuteAsync(async (context, token) => await extensionApiClient.PostAsync(CreateReplayUrl, new FormUrlEncodedContent(payload.Content.Single()), cancellationSource.Token), new Context(), tokenProvider.Token)
                               .ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        string result = await response.Content.ReadAsStringAsync();

                        if (int.TryParse(result, out _))
                        {
                            return result;
                        }
                    }
                    else
                    {
                        logger.LogError($"{response.StatusCode} ({response.ReasonPhrase})");
                    }
                }
            }
            catch (TaskCanceledException)
            {
                logger.LogWarning("Could not create twitch extension replay session because the task was cancelled.");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not create session.");
            }

            return null;
        }

        public async Task<bool> CreatePlayerDataAsync(ExtensionPayload payload, string sessionId, CancellationToken token)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            if (sessionId == null)
                throw new ArgumentNullException(nameof(sessionId));

            List<HttpResponseMessage> responses = new List<HttpResponseMessage>();

            try
            {
                payload.SetGameSessionReplayId(sessionId);

                using (CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, tokenProvider.Token))
                {
                    foreach (var content in payload.Content)
                    {
                        HttpResponseMessage response = await Policy
                            .Handle<Exception>()
                            .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                            .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                            .ExecuteAsync(async (context, token) => await extensionApiClient.PostAsync(CreatePlayerUrl, new FormUrlEncodedContent(content), token), new Context(), cancellationSource.Token)
                            .ConfigureAwait(false);

                        responses.Add(response);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                logger.LogWarning("Could not create twitch extension player data because the task was cancelled.");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not create player data.");
            }

            return responses.All(response => response.IsSuccessStatusCode);
        }

        public async Task<bool> UpdateReplayDataAsync(ExtensionPayload payload, string sessionId, CancellationToken token)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            if (sessionId == null)
                throw new ArgumentNullException(nameof(sessionId));

            try
            {
                payload.SetGameSessionReplayId(sessionId);

                using (CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, tokenProvider.Token))
                {
                    HttpResponseMessage response = await Policy
                               .Handle<Exception>()
                               .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                               .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                               .ExecuteAsync(async (context, token) => await extensionApiClient.PostAsync(UpdateReplayUrl, new FormUrlEncodedContent(payload.Content.Single()), token), new Context(), cancellationSource.Token)
                               .ConfigureAwait(false);

                    return response.IsSuccessStatusCode;
                }
            }
            catch (TaskCanceledException)
            {
                logger.LogWarning("Could not update twitch extension replay data because the task was cancelled.");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not update replay data.");
            }

            return false;
        }

        public async Task<bool> UpdatePlayerDataAsync(ExtensionPayload payload, string sessionId, CancellationToken token)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            if (sessionId == null)
                throw new ArgumentNullException(nameof(sessionId));

            List<HttpResponseMessage> responses = new List<HttpResponseMessage>();

            try
            {
                payload.SetGameSessionReplayId(sessionId);

                using (CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, tokenProvider.Token))
                {
                    foreach (var playerContent in payload.Content)
                    {
                        HttpResponseMessage response = await Policy
                                 .Handle<Exception>()
                                 .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                                 .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                                 .ExecuteAsync(async (context, t) => await extensionApiClient.PostAsync(UpdatePlayerUrl, new FormUrlEncodedContent(playerContent), t), new Context(), cancellationSource.Token)
                                 .ConfigureAwait(false);

                        responses.Add(response);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                logger.LogWarning("Could not update twitch extension player data because the task was cancelled.");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not update player data.");
            }

            return responses.All(x => x.IsSuccessStatusCode);
        }

        public async Task<bool> NotifyTwitchAsync(CancellationToken token)
        {
            try
            {
                using (CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, tokenProvider.Token))
                {
                    HttpResponseMessage response = await Policy
                                     .Handle<Exception>()
                                     .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                                     .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                                     .ExecuteAsync(async (context, token) => await extensionApiClient.PostAsync(NotifyUrl, notifyContent, token), new Context(), cancellationSource.Token)
                                     .ConfigureAwait(false);

                    return response.IsSuccessStatusCode;
                }
            }
            catch (TaskCanceledException)
            {
                logger.LogWarning("Could not notify twitch extension because the task was cancelled.");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not notify.");
            }

            return false;
        }

        public async Task<bool> UpdatePlayerTalentsAsync(List<ExtensionPayload> talentPayloads, string sessionId, CancellationToken token)
        {
            if (talentPayloads == null)
                throw new ArgumentNullException(nameof(talentPayloads));

            if (sessionId == null)
                throw new ArgumentNullException(nameof(sessionId));

            List<HttpResponseMessage> responses = new List<HttpResponseMessage>();

            try
            {
                using (CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, tokenProvider.Token))
                {
                    foreach (var talentPayload in talentPayloads)
                    {
                        talentPayload.SetGameSessionReplayId(sessionId);

                        foreach (var content in talentPayload.Content)
                        {
                            HttpResponseMessage response = await Policy
                                .Handle<Exception>()
                                .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                                .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                                .ExecuteAsync(async (context, token) => await extensionApiClient.PostAsync(SaveTalentUrl, new FormUrlEncodedContent(content), token), new Context(), cancellationSource.Token)
                                .ConfigureAwait(false);

                            responses.Add(response);
                        }
                    }
                }
            }
            catch(TaskCanceledException)
            {
                logger.LogWarning("Could not update twitch extension talents because the task was cancelled.");
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not update talents.");
            }

            return responses.All(response => response.IsSuccessStatusCode);
        }

        public async Task<RewardReplay> GetReplayAsync(GameMode? mode, Tier? tier = null, string map = null)
        {
            try
            {
                string gameType = mode == null ? null : mode switch
                {
                    GameMode.ARAM => "ARAM",
                    GameMode.QuickMatch => "Quick Match",
                    GameMode.Unranked => "Unranked Draft",
                    GameMode.StormLeague => "Storm League",
                    _ => "Quick Match"
                };

                int maxId = await GetMaxReplayIdAsync();
                int startingMinReplayId = maxId - settings.HeroesProfileApi.ApiMaxReturnedReplays;
                var context = new Context("Reward") { { "MinReplayId", startingMinReplayId } };

                RewardReplay rewardReplay = await Policy
                       .Handle<Exception>()
                       .OrResult<RewardReplay>(replay => replay == null)
                       .WaitAndRetryAsync(retryCount: 100, sleepDurationProvider: GetSleepDuration, onRetry: OnRetryGetRewardReplay)
                       .ExecuteAsync(async (context, token) =>
                       {
                           var currentMinReplayId = (int)context["MinReplayId"];

                           var searchContext = new Context($"{currentMinReplayId}:{gameType}")
                           {
                               { "CurrentMinReplayId", currentMinReplayId },
                               { "GameType", gameType }
                           };

                           List<HeroesProfileReplay> replays = await minIdCachePolicy.ExecuteAsync(async (ctx, token) =>
                           {
                               string json = String.Empty;
                               string gameType = (string)ctx["GameType"];
                               int currentMinReplayId = (int)ctx["CurrentMinReplayId"];

                               if (!string.IsNullOrWhiteSpace(gameType))
                               {
                                   json = await apiClient.GetStringAsync(new Uri($"Replay/Min_id?min_id={currentMinReplayId}&game_type={gameType}&api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative));
                               }
                               else
                               {
                                   json = await apiClient.GetStringAsync(new Uri($"Replay/Min_id?min_id={currentMinReplayId}&api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative));
                               }

                               return JsonSerializer
                                    .Deserialize<IEnumerable<HeroesProfileReplay>>(json)
                                    .Where(replay => replay.Deleted == null)
                                    .Where(replay => replay.Parsed == 1) // TODO: Ask Zemil why this always comes back as True
                                    .Where(replay => replay.GameVersion.Equals(settings.Spectate.VersionSupported.ToString()))
                                    .ToList();

                           }, searchContext, tokenProvider.Token);

                           // Random 
                           foreach (var replay in replays.OrderBy(x => Guid.NewGuid()))
                           {
                               ReplayData data = await GetReplayDataAsync(replay.Id);

                               bool isMapMatch = map == null || (data.Map.Equals(map, StringComparison.OrdinalIgnoreCase));
                               bool isTierMatch = tier == null || (tier.HasValue && tier.Value == (Tier)Enum.Parse(typeof(Tier), data.Tier));

                               if (isMapMatch && isTierMatch)
                               {
                                   return new RewardReplay() { ReplayId = replay.Id, Tier = data.Tier, Map = data.Map, GameType = replay.GameType };
                               }
                           }

                           return null;

                       },
                       context, tokenProvider.Token);

                if (rewardReplay != null)
                {
                    return rewardReplay;
                }
            }
            catch (Exception e)
            {

            }

            return null;
        }

        private void OnRetryGetRewardReplay(DelegateResult<RewardReplay> wrappedResponse, TimeSpan timeSpan, int retryAttempt, Context context)
        {
            if (wrappedResponse.Exception == null && wrappedResponse.Result == null)
            {
                int newMinReplayId = ((int)context["MinReplayId"] - settings.HeroesProfileApi.ApiMaxReturnedReplays);
                logger.LogInformation($"No criteria matched. Current MinReplayId: {context["MinReplayId"]}. Next MinReplayId: {newMinReplayId}");
                context["MinReplayId"] = newMinReplayId;
            }
        }

        public async Task<ReplayData> GetReplayDataAsync(int replayId)
        {
            return await replayDataCachePolicy.ExecuteAsync(async (context, token) =>
            {
                var response = await Policy
                    .HandleResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                    .Or<Exception>()
                    .WaitAndRetryAsync(retryCount: 20, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                    .ExecuteAsync((context, token) => apiClient.GetAsync(new Uri($"Replay/Data?mode=json&replayID={replayId}&api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative), token), context, token);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();

                    using (JsonDocument document = JsonDocument.Parse(json))
                    {
                        string map = string.Empty;
                        string gameType = string.Empty;
                        Dictionary<string, float> mmr = new Dictionary<string, float>();

                        foreach (var root in document.RootElement.EnumerateObject())
                        {
                            foreach (var prop in root.Value.EnumerateObject())
                            {
                                if (prop.Name.Equals("game_map"))
                                {
                                    map = prop.Value.GetString();
                                }

                                if (prop.Name.Equals("game_type"))
                                {
                                    gameType = prop.Value.GetString();
                                }

                                if (prop.Value.ValueKind == JsonValueKind.Object && prop.Name.Contains('#'))
                                {
                                    var player = prop.Value.EnumerateObject();

                                    foreach (var field in player)
                                    {
                                        if (field.Name.Equals("player_mmr"))
                                        {
                                            mmr[prop.Name] = field.Value.GetSingle();
                                        }
                                    }
                                }
                            }
                        }

                        int average = Convert.ToInt32(mmr.Values.OrderByDescending(value => value).Take(settings.HeroesProfileApi.MMRPoolSize).Average());

                        string tier = await Policy<string>
                            .Handle<Exception>()
                            .OrResult(tier => string.IsNullOrWhiteSpace(tier))
                            .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: (retrycount) => TimeSpan.FromSeconds(2))
                            .ExecuteAsync(async () =>
                            {
                                var response = await apiClient.GetAsync(new Uri($"MMR/Tier?mmr={average}&game_type={gameType}&api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative));

                                if (response.IsSuccessStatusCode)
                                {
                                    return await response.Content.ReadAsStringAsync();
                                }

                                return null;
                            });

                        return new ReplayData { Map = map, AverageRating = average, ReplayId = replayId, Tier = tier };
                    }
                }

                return null;
            },
            new Context($"ReplayData:{replayId}"), tokenProvider.Token);
        }

        public async Task<HeroesProfileReplay> GetReplayAsync(int replayId)
        {
            return await replayCachePolicy.ExecuteAsync(async (context, token) =>
            {
                HttpResponseMessage response = await Policy
                           .Handle<Exception>()
                           .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                           .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                           .ExecuteAsync(async (context, token) => await apiClient.GetAsync(new Uri($"Replay/Min_id?min_id={replayId}&api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative), token), new Context(), tokenProvider.Token)
                           .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    IEnumerable<HeroesProfileReplay> replays = JsonSerializer.Deserialize<IEnumerable<HeroesProfileReplay>>(await response.Content.ReadAsStringAsync());
                    HeroesProfileReplay replay = replays.FirstOrDefault(replays => replays.Id == replayId);
                    if (replay != null) return replay;
                }

                return null;
            },
            new Context($"HeroesProfileReplay:{replayId}"), tokenProvider.Token);
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
                            .ExecuteAsync((context, token) => apiClient.GetAsync(new Uri($"Replay/Max?api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative), token), context, token);

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
    }
}