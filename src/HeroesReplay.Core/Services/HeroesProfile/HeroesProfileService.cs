﻿using HeroesReplay.Core.Providers;
using HeroesReplay.Core.Shared;

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
        private readonly ReplayHelper replayHelper;
        private readonly HeroesProfileApiSettings settings;

        public HeroesProfileService(ILogger<HeroesProfileService> logger, Settings settings, ReplayHelper replayHelper)
        {
            this.logger = logger;
            this.settings = settings.HeroesProfileApi;
            this.replayHelper = replayHelper;
        }

        public async Task<Uri> GetMatchLink(StormReplay stormReplay)
        {
            var apiKey = settings.ApiKey;

            if (replayHelper.TryGetReplayId(stormReplay, out var replayId))
            {
                return new Uri($"https://www.heroesprofile.com/Match/Single/?replayID={replayId}");
            }

            return new Uri($"https://www.heroesprofile.com/Match/Single/?replayID=");
        }

        public async Task<string> GetMMRTier(StormReplay stormReplay)
        {
            try
            {
                if (replayHelper.TryGetReplayId(stormReplay, out var replayId))
                {
                    var apiKey = settings.ApiKey;

                    using (var client = new HttpClient() { BaseAddress = settings.BaseUri })
                    {
                        string dataResponse = await client.GetStringAsync(new Uri($"Replay/Data?mode=json&replayID={replayId}&api_token={apiKey}", UriKind.Relative)).ConfigureAwait(false);

                        using (JsonDocument dataJson = JsonDocument.Parse(dataResponse))
                        {
                            double average = (from replay in dataJson.RootElement.EnumerateObject()
                                              from element in replay.Value.EnumerateObject()
                                              where element.Value.ValueKind == JsonValueKind.Object
                                              let player = element.Value
                                              from p in player.EnumerateObject()
                                              where p.Name.Equals("player_mmr")
                                              select p.Value.GetDouble())
                                              .OrderByDescending(x => x)
                                              .Take(settings.MMRPoolSize)
                                              .Average();

                            var mmr = Convert.ToInt32(average);

                            return await client.GetStringAsync(new Uri($"MMR/Tier?mmr={mmr}&game_type={stormReplay.GameType}&api_token={apiKey}", UriKind.Relative)).ConfigureAwait(false);
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

        public async Task<IEnumerable<HeroesProfileReplay>> ListReplaysAllAsync(int minId)
        {
            try
            {
                using (var client = new HttpClient() { BaseAddress = settings.OpenApiBaseUri })
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
    }
}