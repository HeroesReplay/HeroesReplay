using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Runner
{
    public class StormReplayDetailsWriter
    {
        private readonly ILogger<StormReplayDetailsWriter> logger;

        public StormReplayDetailsWriter(ILogger<StormReplayDetailsWriter> logger)
        {
            this.logger = logger;
        }

        public async Task WriteDetailsAsync(StormReplay replay)
        {
            using (FileStream mapStream = File.OpenRead(Constants.ASSETS_MAP_JSON_PATH))
            {
                using (JsonDocument mapJson = JsonDocument.Parse(mapStream))
                {
                    using (FileStream heroStream = File.OpenRead(Constants.ASSETS_HEROES_JSON_PATH))
                    {
                        using (JsonDocument heroJson = JsonDocument.Parse(heroStream))
                        {
                            var map = mapJson.RootElement.EnumerateArray()
                                .Where(x => x.GetProperty("short_name").ValueEquals(replay.Replay.MapAlternativeName))
                                .Select(x => x.GetProperty("name").GetString()).SingleOrDefault();

                            var bans =
                                from ban in replay.Replay.DraftOrder.Where(pick => pick.PickType == DraftPickType.Banned).Select((pick, index) => new { Hero = pick.HeroSelected, Index = index + 1 })
                                from hero in heroJson.RootElement.EnumerateObject()
                                let shortName = hero.Value.GetProperty("short_name").GetString()
                                let altName = hero.Value.GetProperty("alt_name").GetString()
                                where ban.Hero.Equals(shortName, StringComparison.OrdinalIgnoreCase) || ban.Hero.Equals(altName, StringComparison.OrdinalIgnoreCase)
                                select $"{hero.Value.GetProperty("name").GetString()}";
                            // select $"Ban {ban.Index}: {hero.Value.GetProperty("name").GetString()}";

                            if (bans.Any()) bans = bans.Prepend("Bans:");

                            string[] details =
                            {
                                // replay.Id.HasValue ? $"Id: {replay.Id.Value}" : string.Empty,
                                // $"Map: {map ?? replay.Replay.MapAlternativeName}",
                                $"{replay.Replay.GameMode switch { GameMode.StormLeague => "Storm League", GameMode.UnrankedDraft => "Unranked", GameMode.QuickMatch => "Quick Match", _ => replay.Replay.GameMode }}",
                                // $"Date: {replay.Replay.Timestamp.Date.ToShortDateString()}"
                                $"{replay.Replay.ReplayVersion}"
                            };

                            logger.LogInformation($"[UPDATE][{Constants.CURRENT_REPLAY_INFORMATION_FILE_PATH}]");

                            // Storm League
                            // MMR
                            // Patch
                            // Bans

                            await File.WriteAllLinesAsync(Constants.CURRENT_REPLAY_INFORMATION_FILE_PATH, details.Concat(bans).Where(line => !string.IsNullOrWhiteSpace(line)), CancellationToken.None);
                        }
                    }
                }
            }
        }
    }
}