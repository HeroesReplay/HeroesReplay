using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using Polly;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class HeroesProfileService : IHeroesProfileService
    {
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

        public HeroesProfileService(ILogger<HeroesProfileService> logger, CancellationTokenProvider tokenProvider, AppSettings settings)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

            this.notifyContent = new(new Dictionary<string, string>
            {
                { settings.TwitchExtension.TwitchApiKey, settings.TwitchExtension.ApiKey },
                { settings.TwitchExtension.TwitchEmailKey, settings.TwitchExtension.ApiEmail },
                { settings.TwitchExtension.TwitchUserNameKey, settings.TwitchExtension.TwitchUserName },
                { settings.TwitchExtension.UserIdKey, settings.TwitchExtension.ApiUserId }
            });
        }

        public Uri GetMatchLink(StormReplay stormReplay) => new Uri($"{stormReplay?.ReplayId}");

        public async Task<(int RankPoints, string Tier)> GetMMRAsync(StormReplay stormReplay)
        {
            if (stormReplay == null)
                throw new ArgumentNullException(nameof(stormReplay));

            try
            {
                if (stormReplay.ReplayId.HasValue)
                {
                    var apiKey = settings.HeroesProfileApi.ApiKey;

                    using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.BaseUri })
                    {
                        string dataResponse = await client.GetStringAsync(new Uri($"Replay/Data?mode=json&replayID={stormReplay.ReplayId.Value}&api_token={apiKey}", UriKind.Relative)).ConfigureAwait(false);

                        using (JsonDocument dataJson = JsonDocument.Parse(dataResponse))
                        {
                            double mmr = (from replay in dataJson.RootElement.EnumerateObject()
                                          from element in replay.Value.EnumerateObject()
                                          where element.Value.ValueKind == JsonValueKind.Object
                                          let player = element.Value
                                          from p in player.EnumerateObject()
                                          where p.Name.Equals(settings.HeroesProfileApi.MMRProperty)
                                          select p.Value.GetDouble())
                                              .OrderByDescending(x => x)
                                              .Take(settings.HeroesProfileApi.MMRPoolSize)
                                              .Average();

                            int average = Convert.ToInt32(mmr);

                            string tier = await client.GetStringAsync(new Uri($"MMR/Tier?mmr={average}&game_type={stormReplay.GameType}&api_token={apiKey}", UriKind.Relative)).ConfigureAwait(false);

                            return (average, tier);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError("Could not calculate average mmr", e);
            }

            return default;
        }

        public async Task<IEnumerable<HeroesProfileReplay>> ListReplaysAllAsync(int minId)
        {
            try
            {
                using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.OpenApiBaseUri })
                {
                    HttpResponseMessage response = await Policy
                           .Handle<Exception>()
                           .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                           .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                           .ExecuteAsync(async (context, token) => await client.GetAsync(new Uri($"Replay/Min_id?min_id={minId}", UriKind.Relative), token), new Context(), tokenProvider.Token)
                           .ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        return JsonSerializer.Deserialize<IEnumerable<HeroesProfileReplay>>(await response.Content.ReadAsStringAsync())
                                             .Where(x => x.Deleted == null || x.Deleted == "0");
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not get replays from HeroesProfile Replays.");
            }

            return Enumerable.Empty<HeroesProfileReplay>();
        }

        public async Task<string> CreateReplaySessionAsync(HeroesProfileTwitchPayload payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            try
            {
                using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.TwitchBaseUri })
                {
                    HttpResponseMessage response = await Policy
                           .Handle<Exception>()
                           .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                           .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                           .ExecuteAsync(async (context, token) => await client.PostAsync(CreateReplayUrl, new FormUrlEncodedContent(payload.Content.Single()), tokenProvider.Token), new Context(), tokenProvider.Token)
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
            catch (Exception e)
            {
                logger.LogError(e, "Could not create session.");
            }

            return null;
        }

        public async Task<bool> CreatePlayerDataAsync(HeroesProfileTwitchPayload payload, string sessionId)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            if (sessionId == null)
                throw new ArgumentNullException(nameof(sessionId));

            List<HttpResponseMessage> responses = new List<HttpResponseMessage>();

            try
            {
                payload.SetGameSessionReplayId(settings.TwitchExtension.ReplayIdKey, sessionId);

                using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.TwitchBaseUri })
                {
                    foreach (var content in payload.Content)
                    {
                        HttpResponseMessage response = await Policy
                            .Handle<Exception>()
                            .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                            .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                            .ExecuteAsync(async (context, token) => await client.PostAsync(CreatePlayerUrl, new FormUrlEncodedContent(content), token), new Context(), tokenProvider.Token)
                            .ConfigureAwait(false);

                        responses.Add(response);
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not create player data.");
            }

            return responses.All(response => response.IsSuccessStatusCode);
        }

        public async Task<bool> UpdateReplayDataAsync(HeroesProfileTwitchPayload payload, string sessionId)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            if (sessionId == null)
                throw new ArgumentNullException(nameof(sessionId));

            try
            {
                payload.SetGameSessionReplayId(settings.TwitchExtension.ReplayIdKey, sessionId);

                using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.TwitchBaseUri })
                {
                    HttpResponseMessage response = await Policy
                           .Handle<Exception>()
                           .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                           .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                           .ExecuteAsync(async (context, token) => await client.PostAsync(UpdateReplayUrl, new FormUrlEncodedContent(payload.Content.Single()), token), new Context(), tokenProvider.Token)
                           .ConfigureAwait(false);

                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not update replay data.");
            }

            return false;
        }

        public async Task<bool> UpdatePlayerDataAsync(HeroesProfileTwitchPayload payload, string sessionId)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            if (sessionId == null)
                throw new ArgumentNullException(nameof(sessionId));

            List<HttpResponseMessage> responses = new List<HttpResponseMessage>();

            try
            {
                payload.SetGameSessionReplayId(settings.TwitchExtension.ReplayIdKey, sessionId);

                using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.TwitchBaseUri })
                {
                    foreach (var playerContent in payload.Content)
                    {
                        HttpResponseMessage response = await Policy
                                 .Handle<Exception>()
                                 .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                                 .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                                 .ExecuteAsync(async (context, token) => await client.PostAsync(UpdatePlayerUrl, new FormUrlEncodedContent(playerContent), token), new Context(), tokenProvider.Token)
                                 .ConfigureAwait(false);

                        responses.Add(response);
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not update player data.");
            }

            return responses.All(x => x.IsSuccessStatusCode);
        }

        public async Task<bool> NotifyTwitchAsync()
        {
            try
            {
                using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.TwitchBaseUri })
                {
                    HttpResponseMessage response = await Policy
                                 .Handle<Exception>()
                                 .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                                 .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                                 .ExecuteAsync(async (context, token) => await client.PostAsync(NotifyUrl, notifyContent, token), new Context(), tokenProvider.Token)
                                 .ConfigureAwait(false);

                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not notify.");
            }

            return false;
        }

        public async Task<bool> UpdatePlayerTalentsAsync(List<HeroesProfileTwitchPayload> talentPayloads, string sessionId)
        {
            if (talentPayloads == null)
                throw new ArgumentNullException(nameof(talentPayloads));

            if (sessionId == null)
                throw new ArgumentNullException(nameof(sessionId));

            List<HttpResponseMessage> responses = new List<HttpResponseMessage>();

            try
            {
                using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.TwitchBaseUri })
                {
                    foreach (var talentPayload in talentPayloads)
                    {
                        talentPayload.SetGameSessionReplayId(settings.TwitchExtension.ReplayIdKey, sessionId);

                        foreach (var content in talentPayload.Content)
                        {
                            HttpResponseMessage response = await Policy
                                .Handle<Exception>()
                                .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                                .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: GetSleepDuration, onRetry: OnRetry)
                                .ExecuteAsync(async (context, token) => await client.PostAsync(SaveTalentUrl, new FormUrlEncodedContent(content), token), new Context(), tokenProvider.Token)
                                .ConfigureAwait(false);

                            responses.Add(response);
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

        private TimeSpan GetSleepDuration(int retry, Context context)
        {
            if (context.ContainsKey("retry-after"))
            {
                var retryAfter = (TimeSpan)context["retry-after"];
                logger.LogInformation($"getting sleep duration for retry attempt {retry}: {retryAfter}");
                return retryAfter;
            }

            var defaultTimeSpan = TimeSpan.FromSeconds(retry * 5);

            logger.LogInformation($"getting default sleep duration for retry attempt {retry}: {defaultTimeSpan}");
            return defaultTimeSpan;
        }

        private void OnRetry(DelegateResult<HttpResponseMessage> wrappedResponse, TimeSpan timeSpan, Context context)
        {
            if (wrappedResponse?.Result?.Headers?.RetryAfter != null)
            {
                TimeSpan retryAfter = wrappedResponse.Result.Headers.RetryAfter.Delta.Value;
                logger.LogInformation($"Setting retry-after to {retryAfter}");
                context["retry-after"] = retryAfter;
            }
        }
    }
}