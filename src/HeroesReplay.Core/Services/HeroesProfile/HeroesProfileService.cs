using HeroesReplay.Core.Providers;
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
        private readonly HeroesProfileApiSettings settings;

        public HeroesProfileService(ILogger<HeroesProfileService> logger, Settings settings)
        {
            this.logger = logger;
            this.settings = settings.HeroesProfileApi;
        }

        public Uri GetMatchLink(StormReplay stormReplay) => new Uri($"{stormReplay?.ReplayId}");

        public async Task<(int RankPoints, string Tier)> GetMMRAsync(StormReplay stormReplay)
        {
            try
            {
                if (stormReplay.ReplayId.HasValue)
                {
                    var apiKey = settings.ApiKey;

                    using (var client = new HttpClient() { BaseAddress = settings.BaseUri })
                    {
                        string dataResponse = await client.GetStringAsync(new Uri($"Replay/Data?mode=json&replayID={stormReplay.ReplayId.Value}&api_token={apiKey}", UriKind.Relative)).ConfigureAwait(false);

                        using (JsonDocument dataJson = JsonDocument.Parse(dataResponse))
                        {
                            double mmr = (from replay in dataJson.RootElement.EnumerateObject()
                                              from element in replay.Value.EnumerateObject()
                                              where element.Value.ValueKind == JsonValueKind.Object
                                              let player = element.Value
                                              from p in player.EnumerateObject()
                                              where p.Name.Equals(settings.MMRProperty)
                                              select p.Value.GetDouble())
                                              .OrderByDescending(x => x)
                                              .Take(settings.MMRPoolSize)
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

            return default((int RankPoints, string Tier));
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