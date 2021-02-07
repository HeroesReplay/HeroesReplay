using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;

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
        private readonly AppSettings settings;

        private const string CreateReplayUrl = @"save/replay";
        private const string UpdateReplayUrl = @"update/replay/";
        private const string CreatePlayerUrl = @"save/player";
        private const string UpdatePlayerUrl = @"update/player";
        private const string SaveTalentUrl = @"save/talent";
        private const string NotifyUrl = @"notify/talent/update";

        public HeroesProfileService(ILogger<HeroesProfileService> logger, AppSettings settings)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public Uri GetMatchLink(StormReplay stormReplay) => new Uri($"{stormReplay?.ReplayId}");

        public async Task<string> CreateReplaySessionAsync(HeroesProfileTwitchPayload payload)
        {
            // TODO: Polly
            using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.TwitchBaseUri })
            {
                var response = await client.PostAsync(CreateReplayUrl, new FormUrlEncodedContent(payload.Content.Single()));

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

            return null;
        }

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
                    var json = await client.GetStringAsync(new Uri($"Replay/Min_id?min_id={minId}", UriKind.Relative)).ConfigureAwait(false);
                    return JsonSerializer.Deserialize<IEnumerable<HeroesProfileReplay>>(json).Where(x => x.Deleted == null || x.Deleted == "0");
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not get replays from HeroesProfile Replays.");
            }

            return Enumerable.Empty<HeroesProfileReplay>();
        }

        public async Task CreatePlayerDataAsync(HeroesProfileTwitchPayload payload, string sessionId)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            if (sessionId == null)
                throw new ArgumentNullException(nameof(sessionId));

            try
            {
                payload.SetGameSessionReplayId(settings.TwitchExtension.ReplayIdKey, sessionId);

                // TODO: Polly
                using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.TwitchBaseUri })
                {
                    foreach (var content in payload.Content)
                    {
                        var response = await client.PostAsync(CreatePlayerUrl, new FormUrlEncodedContent(content));

                        if (response.IsSuccessStatusCode)
                        {
                            logger.LogInformation("Player data created.");
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
                logger.LogError(e, "Could not create player data.");
            }
        }

        public async Task UpdateReplayDataAsync(HeroesProfileTwitchPayload payload, string sessionId)
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
                    var response = await client.PostAsync(UpdateReplayUrl, new FormUrlEncodedContent(payload.Content.Single()));

                    if (response.IsSuccessStatusCode)
                    {
                        logger.LogInformation("Replay data updated.");
                    }
                    else
                    {
                        logger.LogError($"{response.StatusCode} ({response.ReasonPhrase})");
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not update replay data.");
            }
        }

        public async Task UpdatePlayerDataAsync(HeroesProfileTwitchPayload payload, string sessionId)
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
                    foreach (var playerContent in payload.Content)
                    {
                        var response = await client.PostAsync(UpdatePlayerUrl, new FormUrlEncodedContent(playerContent));

                        if (response.IsSuccessStatusCode)
                        {
                            logger.LogInformation("Player data created.");
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
                logger.LogError(e, "Could not update player data.");
            }
        }

        public async Task UpdatePlayerTalentsAsync(List<HeroesProfileTwitchPayload> talentPayloads, string sessionId)
        {
            if (talentPayloads == null)
                throw new ArgumentNullException(nameof(talentPayloads));

            if (sessionId == null)
                throw new ArgumentNullException(nameof(sessionId));

            try
            {
                using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileApi.TwitchBaseUri })
                {
                    foreach (var talentPayload in talentPayloads)
                    {
                        talentPayload.SetGameSessionReplayId(settings.TwitchExtension.ReplayIdKey, sessionId);

                        foreach (var content in talentPayload.Content)
                        {
                            Dictionary<string, string> clone = new Dictionary<string, string>();

                            foreach (var item in content)
                            {
                                if (item.Key == settings.TwitchExtension.TwitchApiKey ||
                                    item.Key == settings.TwitchExtension.TwitchEmailKey ||
                                    item.Key == settings.TwitchExtension.UserIdKey)
                                {
                                    continue;
                                }

                                clone[item.Key] = item.Value;
                            }

                            var talentJson = JsonSerializer.Serialize(clone, typeof(Dictionary<string, string>), new JsonSerializerOptions { WriteIndented = true });

                            logger.LogInformation(talentJson);

                            var talentResponse = await client.PostAsync(SaveTalentUrl, new FormUrlEncodedContent(content));

                            if (talentResponse.IsSuccessStatusCode)
                            {
                                logger.LogInformation("Talent updated.");
                            }
                            else
                            {
                                logger.LogError($"Talent failure: {talentResponse.StatusCode} ({talentResponse.ReasonPhrase})");
                            }
                        }
                    }

                    var notifyResponse = await client.PostAsync(NotifyUrl, new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { settings.TwitchExtension.TwitchApiKey, settings.TwitchExtension.ApiKey },
                        { settings.TwitchExtension.TwitchEmailKey, settings.TwitchExtension.ApiEmail },
                        { settings.TwitchExtension.TwitchUserNameKey, settings.TwitchExtension.TwitchUserName },
                        { settings.TwitchExtension.UserIdKey, settings.TwitchExtension.ApiUserId },
                    }));

                    if (notifyResponse.IsSuccessStatusCode)
                    {
                        logger.LogInformation("Notify successful.");
                    }
                    else
                    {
                        logger.LogError($"Notify failure: {notifyResponse.StatusCode} ({notifyResponse.ReasonPhrase})");
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not update talents.");
            }
        }
    }
}