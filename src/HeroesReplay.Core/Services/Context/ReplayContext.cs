
namespace HeroesReplay.Core.Services.Context
{
    using System;
    using System.IO;
    using System.Threading.Tasks;

    using Heroes.ReplayParser;

    using HeroesReplay.Core.Configuration;
    using HeroesReplay.Core.Models;
    using HeroesReplay.Core.Services.Analysis;

    using Microsoft.Extensions.Logging;

    public class ReplayContext : IReplayContext, IReplayContextSetter
    {
        public ContextData Previous { get; private set; }
        public ContextData Current { get; private set; }

        private readonly ILogger<ReplayContext> logger;
        private readonly IContextFileManager contextFileManager;
        private readonly IReplayAnalyzer replayAnalyzer;
        private readonly AppSettings settings;

        public ReplayContext(ILogger<ReplayContext> logger, IContextFileManager contextFileManager, IReplayAnalyzer replayAnalyzer, AppSettings settings)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.contextFileManager = contextFileManager;
            this.replayAnalyzer = replayAnalyzer ?? throw new ArgumentNullException(nameof(replayAnalyzer));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task SetContextAsync(LoadedReplay loadedReplay)
        {
            if (loadedReplay == null)
                throw new ArgumentNullException(nameof(loadedReplay));

            Previous = Current;

            Replay replay = loadedReplay.Replay;

            var players = replayAnalyzer.GetPlayers(replay);
            var panels = replayAnalyzer.GetPanels(replay);
            var end = replayAnalyzer.GetEnd(replay);
            var isCarried = replayAnalyzer.GetIsCarriedObjective(replay);
            var start = replayAnalyzer.GetStart(replay);
            var payloads = replayAnalyzer.GetPayloads(replay);
            var teamBans = replayAnalyzer.GetTeamBans(replay);
            var directory = Directory.CreateDirectory(settings.ContextsDirectory).CreateSubdirectory($"{loadedReplay.ReplayId}");

            Current = new ContextData
            {
                LoadedReplay = loadedReplay,
                Payloads = payloads,
                Players = players,
                Panels = panels,
                GatesOpen = start,
                CoreKilled = end,
                IsCarriedObjectiveMap = isCarried,
                Timeloaded = DateTime.Now,
                Directory = directory,
                TeamBans = teamBans
            };

            await contextFileManager.WriteContextFilesAsync(Current);
        }
    }
}