
using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;

using System;

namespace HeroesReplay.Core
{
    public class ReplayContext : IReplayContext, IReplayContextSetter
    {
        public SessionData Previous { get; private set; }
        public SessionData Current { get; private set; }

        private readonly ILogger<ReplayContext> logger;
        private readonly IReplayAnalyzer replayAnalyzer;

        public ReplayContext(ILogger<ReplayContext> logger, IReplayAnalyzer replayAnalyzer)
        {
            this.logger = logger;
            this.replayAnalyzer = replayAnalyzer;
        }

        public void SetContext(LoadedReplay loadedReplay)
        {
            if (loadedReplay == null)
                throw new ArgumentNullException(nameof(loadedReplay));

            Previous = Current;

            var players = replayAnalyzer.GetPlayers(loadedReplay.Replay);
            var panels = replayAnalyzer.GetPanels(loadedReplay.Replay);
            var end = replayAnalyzer.GetEnd(loadedReplay.Replay);
            var isCarried = replayAnalyzer.GetIsCarriedObjective(loadedReplay.Replay);
            var start = replayAnalyzer.GetStart(loadedReplay.Replay);
            var payloads = replayAnalyzer.GetPayloads(loadedReplay.Replay);

            Current = new SessionData
            {
                LoadedReplay = loadedReplay,
                Payloads = payloads,
                Players = players,
                Panels = panels,
                GatesOpen = start,
                CoreKilled = end,
                IsCarriedObjectiveMap = isCarried,
                Timeloaded = DateTime.Now
            };
        }
    }
}