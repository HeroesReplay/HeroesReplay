using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Runner
{
    public class StormReplayMMRCalculator
    {
        private readonly ILogger<StormReplayMMRCalculator> logger;
        private readonly IConfiguration configuration;

        public StormReplayMMRCalculator(ILogger<StormReplayMMRCalculator> logger, IConfiguration configuration)
        {
            this.logger = logger;
            this.configuration = configuration;
        }

        public async Task<int> CalculateMMRAsync(StormReplay stormReplay)
        {
            var apiKey = configuration.GetValue<string>(Constants.ConfigKeys.HeroesProfileApiKey);
            var hotsApiReplayId = stormReplay.TryGetReplayId();

            using (var client = new HttpClient() { BaseAddress = new Uri("https://api.heroesprofile.com/api/") })
            {
                string heroesProfileReplayId = await client.GetStringAsync($"Heroesprofile/ReplayID?hotsapi_replayID={hotsApiReplayId}&api_token={apiKey}").ConfigureAwait(false);

                logger.LogDebug($"HotsAPI Replay ID: {hotsApiReplayId}. HeroesProfile Replay ID: {heroesProfileReplayId}");

                string dataResponse = await client.GetStringAsync($"Replay/Data?mode=json&replayID={heroesProfileReplayId}&api_token={apiKey}").ConfigureAwait(false);

                using (JsonDocument dataJson = JsonDocument.Parse(dataResponse))
                {
                    double average = (from replay in dataJson.RootElement.EnumerateObject()
                                      from element in replay.Value.EnumerateObject()
                                      where element.Value.ValueKind == JsonValueKind.Object
                                      let player = element.Value
                                      from p in player.EnumerateObject()
                                      where p.Name.Equals("player_mmr")
                                      select p.Value.GetDouble()).Average();

                    return Convert.ToInt32(average);
                }
            }
        }
    }
}