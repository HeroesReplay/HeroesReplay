using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Runner
{
    public class ReplayDetailsWriter
    {
        private readonly ILogger<ReplayDetailsWriter> logger;
        private readonly ReplayHelper replayHelper;
        private readonly HeroesProfileService heroesProfileService;
        private readonly GameDataService gameDataService;

        public ReplayDetailsWriter(ILogger<ReplayDetailsWriter> logger, ReplayHelper replayHelper, HeroesProfileService heroesProfileService, GameDataService gameDataService)
        {
            this.logger = logger;
            this.replayHelper = replayHelper;
            this.heroesProfileService = heroesProfileService;
            this.gameDataService = gameDataService;
        }

        public async Task WriteDetailsAsync(StormReplay replay)
        {
            var mmr = await heroesProfileService.CalculateMMRAsync(replay);
            var map = gameDataService.Maps.Find(map => map.AltName.Equals(replay.Replay.MapAlternativeName) || replay.Replay.Map.Equals(map.Name));
            var mode = replay.Replay.GameMode switch { GameMode.StormLeague => "Storm League", GameMode.UnrankedDraft => "Unranked", GameMode.QuickMatch => "Quick Match", _ => replay.Replay.GameMode.ToString() };

            var bans = from ban in replay.Replay.DraftOrder.Where(pick => pick.PickType == DraftPickType.Banned).Select((pick, index) => new { Hero = pick.HeroSelected, Index = index + 1 })
                       from hero in gameDataService.Heroes
                       where ban.Hero.Equals(hero.Name, StringComparison.OrdinalIgnoreCase) || ban.Hero.Equals(hero.AltName, StringComparison.OrdinalIgnoreCase)
                       select $"{hero.Name}";

            if (bans.Any()) bans = bans.Prepend("Bans:");

            string[] details = { mode, $"MMR: {mmr}", replay.Replay.ReplayVersion };

            logger.LogInformation($"WriteDetailsAsync: {replayHelper.CurrentReplayPath}");

            await File.WriteAllLinesAsync(replayHelper.CurrentReplayPath, details.Concat(bans).Where(line => !string.IsNullOrWhiteSpace(line)), CancellationToken.None);
        }
    }
}