using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Runner
{
    public class HeroesProfileService
    {
        private readonly ILogger<HeroesProfileService> logger;
        private readonly Settings settings;
        private readonly ReplayHelper replayHelper;

        public HeroesProfileService(ILogger<HeroesProfileService> logger, IOptions<Settings> settings, ReplayHelper replayHelper)
        {
            this.logger = logger;
            this.settings = settings.Value;
            this.replayHelper = replayHelper;
        }

        public async Task<Uri> GetMatchLink(StormReplay stormReplay)
        {
            var apiKey = settings.HeroesProfileApiKey;
            var hotsApiReplayId = replayHelper.TryGetReplayId(stormReplay);

            using (var client = new HttpClient() { BaseAddress = settings.HeroesProfileBaseUri })
            {
                string replayId = await client.GetStringAsync($"Heroesprofile/ReplayID?hotsapi_replayID={hotsApiReplayId}&api_token={apiKey}").ConfigureAwait(false);

                return new Uri($"https://www.heroesprofile.com/Match/Single/?replayID={replayId}");
            }
        }

        public async Task<string> CalculateMMRAsync(StormReplay stormReplay)
        {
            try
            {
                var apiKey = settings.HeroesProfileApiKey;
                var hotsApiReplayId = replayHelper.TryGetReplayId(stormReplay);

                using (var client = new HttpClient() { BaseAddress = new Uri("https://api.heroesprofile.com/api/") })
                {
                    string heroesProfileReplayId = await client.GetStringAsync($"Heroesprofile/ReplayID?hotsapi_replayID={hotsApiReplayId}&api_token={apiKey}").ConfigureAwait(false);

                    logger.LogDebug($"HotsAPI ID: {hotsApiReplayId}. HeroesProfile ID: {heroesProfileReplayId}");

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

                        var mmr = Convert.ToInt32(average);

                        return await client.GetStringAsync($"MMR/Tier?mmr={mmr}&game_type={"Storm League"}&api_token={apiKey}").ConfigureAwait(false);
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError("Could not calculate average mmr", e);

                return "Unknown";
            }
        }
    }
}