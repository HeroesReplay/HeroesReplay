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

        public HeroesProfileService(ILogger<HeroesProfileService> logger, AppSettings settings)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
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