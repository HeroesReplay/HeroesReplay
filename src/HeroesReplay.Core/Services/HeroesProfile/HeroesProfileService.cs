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
        private readonly ILogger<HeroesProfileService> logger;
        private readonly IAsyncCacheProvider cacheProvider;
        private readonly CancellationTokenProvider tokenProvider;
        private readonly AppSettings settings;

        private const string CreateReplayUrl = @"save/replay";
        private const string UpdateReplayUrl = @"update/replay/";
        private const string CreatePlayerUrl = @"save/player";
        private const string UpdatePlayerUrl = @"update/player";
        private const string SaveTalentUrl = @"save/talent";
        private const string NotifyUrl = @"notify/talent/update";

        private readonly FormUrlEncodedContent notifyContent;

        public HeroesProfileService(ILogger<HeroesProfileService> logger, IAsyncCacheProvider cacheProvider, CancellationTokenProvider tokenProvider, AppSettings settings)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.cacheProvider = cacheProvider ?? throw new ArgumentNullException(nameof(cacheProvider));
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

            notifyContent = new(new Dictionary<string, string>
            {
                { ExtensionFormKeys.TwitchKey, settings.TwitchExtension.ApiKey },
                { ExtensionFormKeys.Email, settings.TwitchExtension.ApiEmail },
                { ExtensionFormKeys.TwitchUserName, settings.TwitchExtension.TwitchUserName },
                { ExtensionFormKeys.UserId, settings.TwitchExtension.ApiUserId }
            });
        }

        public async Task<IEnumerable<HeroesProfileReplay>> ListReplaysAllAsync(int minId)
        {
            try
            {
                using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.BaseUri })
                {
                    HttpResponseMessage response = await Policy
                           .Handle<Exception>()
                           .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                           .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                           .ExecuteAsync(async (context, token) => await client.GetAsync(new Uri($"Replay/Min_id?min_id={minId}&api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative), token), new Context(), tokenProvider.Token)
                           .ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        logger.LogInformation("Deserializing replays...");

                        return JsonSerializer.Deserialize<IEnumerable<HeroesProfileReplay>>(await response.Content.ReadAsStringAsync()).Where(x => x.Deleted == null);
                    }
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
                    using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.TwitchBaseUri })
                    {
                        HttpResponseMessage response = await Policy
                               .Handle<Exception>()
                               .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                               .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                               .ExecuteAsync(async (context, token) => await client.PostAsync(CreateReplayUrl, new FormUrlEncodedContent(payload.Content.Single()), cancellationSource.Token), new Context(), tokenProvider.Token)
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
                    using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.TwitchBaseUri })
                    {
                        foreach (var content in payload.Content)
                        {
                            HttpResponseMessage response = await Policy
                                .Handle<Exception>()
                                .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                                .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                                .ExecuteAsync(async (context, token) => await client.PostAsync(CreatePlayerUrl, new FormUrlEncodedContent(content), token), new Context(), cancellationSource.Token)
                                .ConfigureAwait(false);

                            responses.Add(response);
                        }
                    }
                }


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
                    using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.TwitchBaseUri })
                    {
                        HttpResponseMessage response = await Policy
                               .Handle<Exception>()
                               .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                               .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                               .ExecuteAsync(async (context, token) => await client.PostAsync(UpdateReplayUrl, new FormUrlEncodedContent(payload.Content.Single()), token), new Context(), cancellationSource.Token)
                               .ConfigureAwait(false);

                        return response.IsSuccessStatusCode;
                    }
                }
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
                    using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.TwitchBaseUri })
                    {
                        foreach (var playerContent in payload.Content)
                        {
                            HttpResponseMessage response = await Policy
                                     .Handle<Exception>()
                                     .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                                     .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                                     .ExecuteAsync(async (context, t) => await client.PostAsync(UpdatePlayerUrl, new FormUrlEncodedContent(playerContent), t), new Context(), cancellationSource.Token)
                                     .ConfigureAwait(false);

                            responses.Add(response);
                        }
                    }
                }


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
                    using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.TwitchBaseUri })
                    {
                        HttpResponseMessage response = await Policy
                                     .Handle<Exception>()
                                     .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                                     .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                                     .ExecuteAsync(async (context, token) => await client.PostAsync(NotifyUrl, notifyContent, token), new Context(), cancellationSource.Token)
                                     .ConfigureAwait(false);

                        return response.IsSuccessStatusCode;
                    }
                }
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
                    using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.TwitchBaseUri })
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
                                    .ExecuteAsync(async (context, token) => await client.PostAsync(SaveTalentUrl, new FormUrlEncodedContent(content), token), new Context(), cancellationSource.Token)
                                    .ConfigureAwait(false);

                                responses.Add(response);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not update talents.");
            }

            return responses.All(response => response.IsSuccessStatusCode);
        }

        public async Task<HeroesProfileReplay> GetReplayAsync(GameMode? mode, Tier? tier = null, Map map = null)
        {
            try
            {
                string gameType = (mode == null ? null : mode switch
                {
                    GameMode.ARAM => "ARAM",
                    GameMode.QuickMatch => "Quick Match",
                    GameMode.Unranked => "Unranked Draft",
                    GameMode.StormLeague => "Storm League",
                    _ => "Quick Match"
                });

                int maxId = await GetMaxReplayIdAsync();
                int minId = maxId - settings.HeroesProfileApi.ApiMaxReturnedReplays;
                var context = new Context() { { "MinReplayId", minId } };

                using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.BaseUri })
                {
                    return await Policy
                           .Handle<Exception>()
                           .OrResult<HeroesProfileReplay>(replay => replay == null)
                           .WaitAndRetryAsync(retryCount: 60, sleepDurationProvider: GetSleepDuration, onRetry: OnGetReplayRetry)
                           .ExecuteAsync(async (context, token) =>
                           {
                               var minId = context["MinReplayId"];
                               var json = await client.GetStringAsync(new Uri($"Replay/Min_id?min_id={minId}&api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative));
                               var replays = JsonSerializer.Deserialize<IEnumerable<HeroesProfileReplay>>(json);

                               replays = replays.Where(replay => replay.Deleted == null);
                               replays = replays.Where(replay => replay.GameVersion.Equals(settings.Spectate.VersionSupported.ToString()));

                               if (!string.IsNullOrWhiteSpace(gameType))
                               {
                                   replays = replays.Where(replay => replay.GameType.Equals(gameType, StringComparison.OrdinalIgnoreCase));
                               }

                               replays = replays.OrderBy(replay => Guid.NewGuid());

                               foreach (var replay in replays.ToList())
                               {
                                   ReplayData data = await GetReplayDataAsync(replay.Id);

                                   bool mapMatches = map == null || (data.Map.Equals(map.Name, StringComparison.OrdinalIgnoreCase) || data.Map.Equals(map.AltName, StringComparison.OrdinalIgnoreCase));
                                   bool tierMatches = tier == null || (tier.HasValue && tier.Value == (Tier)Enum.Parse(typeof(Tier), data.Tier));

                                   if (mapMatches && tierMatches)
                                   {
                                       logger.LogInformation($"Replay found with criteria: {replay.Id}");
                                       return replay;
                                   }
                               }

                               return null;
                           },
                           context, tokenProvider.Token);
                }
            }
            catch (Exception e)
            {

            }

            throw new Exception("Could not get replay from HeroesProfile Replays");
        }

        private void OnGetReplayRetry(DelegateResult<HeroesProfileReplay> wrappedResponse, TimeSpan timeSpan, int retryAttempt, Context context)
        {
            if (wrappedResponse.Exception == null && wrappedResponse.Result == null)
            {
                int newMinReplayId = ((int)context["MinReplayId"] - settings.HeroesProfileApi.ApiMaxReturnedReplays);
                logger.LogDebug($"No criteria matched. Current MinReplayId: {context["MinReplayId"]}. Next MinReplayId: {newMinReplayId}");
                context["MinReplayId"] = ((int)context["MinReplayId"] - settings.HeroesProfileApi.ApiMaxReturnedReplays);
            }
        }

        public async Task<ReplayData> GetReplayDataAsync(int replayId)
        {
            using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.BaseUri })
            {
                HttpResponseMessage response = await Policy
                       .Handle<Exception>()
                       .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                       .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                       .ExecuteAsync(async (context, token) => await client.GetAsync(new Uri($"Replay/Data?mode=json&replayID={replayId}&api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative), token), new Context(), tokenProvider.Token)
                       .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();

                    using (var doc = JsonDocument.Parse(json))
                    {
                        string map = string.Empty;
                        string gameType = string.Empty;
                        Dictionary<string, float> mmr = new Dictionary<string, float>();

                        foreach (var root in doc.RootElement.EnumerateObject())
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

                        string tier = await Policy
                            .Handle<Exception>()
                            .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: (retrycount) => TimeSpan.FromSeconds(2))
                            .ExecuteAsync(() => client.GetStringAsync(new Uri($"MMR/Tier?mmr={average}&game_type={gameType}&api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative)));

                        return new ReplayData { Map = map, AverageMmr = average, ReplayId = replayId, PlayerMmrs = mmr, Tier = tier };
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="replayId"></param>
        /// <exception cref="ReplayDeletedException">When the replay was found but the raw asset is deleted.</exception>
        /// <exception cref="ReplayVersionNotSupportedException">When the replay was found but the raw asset is deleted.</exception>
        /// <exception cref="ReplayNotFoundException">When the replay was found but the raw asset is deleted.</exception>
        /// <returns></returns>
        public async Task<HeroesProfileReplay> GetReplayAsync(int replayId)
        {
            try
            {
                using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.BaseUri })
                {
                    HttpResponseMessage response = await Policy
                           .Handle<Exception>()
                           .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                           .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                           .ExecuteAsync(async (context, token) => await client.GetAsync(new Uri($"Replay/Min_id?min_id={replayId}&api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative), token), new Context(), tokenProvider.Token)
                           .ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        logger.LogInformation("Deserializing replays...");

                        IEnumerable<HeroesProfileReplay> replays = JsonSerializer.Deserialize<IEnumerable<HeroesProfileReplay>>(await response.Content.ReadAsStringAsync());

                        HeroesProfileReplay replay = replays.FirstOrDefault(replays => replays.Id == replayId);

                        if (replay != null)
                        {
                            if (replay.Deleted != null)
                            {
                                throw new ReplayDeletedException($"The raw replay file is no longer available.");
                            }

                            bool versionSupported = (replay.GameVersion ?? string.Empty).Equals(settings.Spectate.VersionSupported.ToString());

                            if (!versionSupported)
                            {
                                throw new ReplayVersionNotSupportedException($"Version found: {replay.GameVersion} but expected {settings.Spectate.VersionSupported}.");
                            }

                            if (replay.Deleted == null)
                            {
                                return replay;
                            }
                        }
                    }
                }
            }
            catch (Exception e) when (!knownExceptions.Contains(e.GetType()))
            {
                logger.LogError(e, $"Error in retrieving replayid: {replayId}");
            }
            catch (Exception e) when (knownExceptions.Contains(e.GetType()))
            {
                throw;
            }

            throw new ReplayNotFoundException($"Could not find replay: {replayId}");
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

        private async Task<int> GetMaxReplayIdAsync()
        {
            try
            {
                using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.BaseUri })
                {
                    HttpResponseMessage response = await Policy
                        .CacheAsync(cacheProvider.AsyncFor<HttpResponseMessage>(), TimeSpan.FromHours(12), OnCacheError)
                        .ExecuteAsync(() => Policy
                           .Handle<Exception>()
                           .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                           .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                           .ExecuteAsync((context, token) => client.GetAsync(new Uri($"Replay/Max?api_token={settings.HeroesProfileApi.ApiKey}", UriKind.Relative), token), new Context(), tokenProvider.Token))
                        .ConfigureAwait(false);

                    string content = await response.Content.ReadAsStringAsync();

                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        if (int.TryParse(content, out int maxId))
                        {
                            return maxId;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not get max replayId from HeroesProfile API.");
            }

            return settings.HeroesProfileApi.FallbackMaxReplayId;
        }

        private void OnCacheError(Context context, string key, Exception exception)
        {
            logger.LogError(exception, $"Could not cache {key}");
        }

        private readonly Type[] knownExceptions = new Type[] { typeof(ReplayDeletedException), typeof(ReplayVersionNotSupportedException) };
    }
}