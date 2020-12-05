﻿using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Runner
{
    public class HeroesProfileService
    {
        private readonly ILogger<HeroesProfileService> logger;        
        private readonly ReplayHelper replayHelper;
        private readonly HeroesProfileApiSettings settings;

        public HeroesProfileService(ILogger<HeroesProfileService> logger, Settings settings, ReplayHelper replayHelper)
        {
            this.logger = logger;
            this.settings = settings.HeroesProfileApiSettings;
            this.replayHelper = replayHelper;
        }

        public async Task<Uri> GetMatchLink(StormReplay stormReplay)
        {
            var apiKey = settings.HeroesProfileApiKey;

            if (replayHelper.TryGetReplayId(stormReplay, out var hotsApiReplayId))
            {
                using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileBaseUri })
                {
                    string replayId = await client.GetStringAsync(new Uri($"Heroesprofile/ReplayID?hotsapi_replayID={hotsApiReplayId}&api_token={apiKey}", UriKind.Relative)).ConfigureAwait(false);

                    return new Uri($"https://www.heroesprofile.com/Match/Single/?replayID={replayId}");
                }
            }

            return new Uri($"https://www.heroesprofile.com/Match/Single/?replayID=");
        }

        public async Task<string> CalculateMMRAsync(StormReplay stormReplay)
        {
            try
            {
                if (replayHelper.TryGetReplayId(stormReplay, out var hotsApiReplayId))
                {
                    var apiKey = settings.HeroesProfileApiKey;

                    using (var client = new HttpClient() { BaseAddress = new Uri("https://api.heroesprofile.com/api/") })
                    {
                        string heroesProfileReplayId = await client.GetStringAsync(new Uri($"Heroesprofile/ReplayID?hotsapi_replayID={hotsApiReplayId}&api_token={apiKey}", UriKind.Relative)).ConfigureAwait(false);

                        logger.LogDebug($"HotsAPI ID: {hotsApiReplayId}. HeroesProfile ID: {heroesProfileReplayId}");

                        string dataResponse = await client.GetStringAsync(new Uri($"Replay/Data?mode=json&replayID={heroesProfileReplayId}&api_token={apiKey}", UriKind.Relative)).ConfigureAwait(false);

                        using (JsonDocument dataJson = JsonDocument.Parse(dataResponse))
                        {
                            double average = (from replay in dataJson.RootElement.EnumerateObject()
                                              from element in replay.Value.EnumerateObject()
                                              where element.Value.ValueKind == JsonValueKind.Object
                                              let player = element.Value
                                              from p in player.EnumerateObject()
                                              where p.Name.Equals("player_mmr")
                                              select p.Value.GetDouble()).Average();

                            var mmr = Convert.ToInt32(average);

                            return await client.GetStringAsync(new Uri($"MMR/Tier?mmr={mmr}&game_type={"Storm League"}&api_token={apiKey}", UriKind.Relative)).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError("Could not calculate average mmr", e);
            }

            return "Unknown";
        }
    }
}