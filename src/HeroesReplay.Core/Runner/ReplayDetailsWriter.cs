using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Runner
{
    public class ReplayDetailsWriter
    {
        private readonly ILogger<ReplayDetailsWriter> logger;
        private readonly HeroesProfileService heroesProfileService;
        private readonly GameDataService gameDataService;
        private readonly Settings settings;

        public ReplayDetailsWriter(ILogger<ReplayDetailsWriter> logger, IOptions<Settings> settings, ReplayHelper replayHelper, HeroesProfileService heroesProfileService, GameDataService gameDataService)
        {
            this.logger = logger;
            this.heroesProfileService = heroesProfileService;
            this.gameDataService = gameDataService;
            this.settings = settings.Value;
        }

        public async Task WriteDetailsAsync(StormReplay replay)
        {
            var mmr = settings.EnableMMR ? $"MMR: " + await heroesProfileService.CalculateMMRAsync(replay) : string.Empty;
            var map = gameDataService.Maps.Find(map => map.AltName.Equals(replay.Replay.MapAlternativeName) || replay.Replay.Map.Equals(map.Name));
            
            var mode = replay.Replay.GameMode switch
            {
                //GameMode.StormLeague => "Storm League",
                //GameMode.UnrankedDraft => "Unranked",
                //GameMode.QuickMatch => "Quick Match",
                //_ => replay.Replay.GameMode.ToString()
                _ => string.Empty
            };

            var bans = from ban in replay.Replay.DraftOrder.Where(pick => pick.PickType == DraftPickType.Banned).Select((pick, index) => new { Hero = pick.HeroSelected, Index = index + 1 })
                       from hero in gameDataService.Heroes
                       where ban.Hero.Equals(hero.Name, StringComparison.OrdinalIgnoreCase) || ban.Hero.Equals(hero.AltName, StringComparison.OrdinalIgnoreCase)
                       select $"{hero.Name}";

            if (bans.Any()) bans = bans.Prepend("Bans:");

            string[] details = new[] { mode, mmr, replay.Replay.ReplayVersion }.Where(line => !string.IsNullOrEmpty(line)).ToArray();

            logger.LogInformation($"WriteDetailsAsync: {settings.CurrentReplayPath}");

            await File.WriteAllLinesAsync(settings.CurrentReplayPath, details.Concat(bans).Where(line => !string.IsNullOrWhiteSpace(line)), CancellationToken.None);
        }
    }
}