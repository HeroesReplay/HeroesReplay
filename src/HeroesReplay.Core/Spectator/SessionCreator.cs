using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;

using Microsoft.Extensions.Logging;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public class SessionCreator : ISessionCreator
    {
        private readonly IReplayAnalzer replayAnalyzer;
        private readonly ISessionSetter sessionSetter;
        private readonly ILogger<SessionCreator> logger;
        private readonly IHeroesProfileService heroesProfileService;

        public SessionCreator(ILogger<SessionCreator> logger, IHeroesProfileService heroesProfileService, IReplayAnalzer replayAnalyzer, ISessionSetter sessionSetter)
        {
            this.replayAnalyzer = replayAnalyzer ?? throw new ArgumentNullException(nameof(replayAnalyzer));
            this.sessionSetter = sessionSetter ?? throw new ArgumentNullException(nameof(sessionSetter));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.heroesProfileService = heroesProfileService;
        }

        public async Task CreateAsync(StormReplay stormReplay)
        {
            if (stormReplay == null)
                throw new ArgumentNullException(nameof(stormReplay));

            DateTime started = DateTime.Now;

            logger.LogInformation($"Creating session for: {stormReplay.Path}");

            var players = replayAnalyzer.GetPlayers(stormReplay.Replay);
            var panels = replayAnalyzer.GetPanels(stormReplay.Replay);
            var end = replayAnalyzer.GetEnd(stormReplay.Replay);
            var carriedObjectives = replayAnalyzer.IsCarriedObjectiveMap(stormReplay.Replay);
            var start = replayAnalyzer.GetStart(stormReplay.Replay);
            var payloads = replayAnalyzer.GetPayloads(stormReplay.Replay);
            var replayId = stormReplay.ReplayId;
            var gameType = stormReplay.GameType;

            ReplayData replayData = await heroesProfileService.GetReplayDataAsync(replayId.Value);

            sessionSetter.SetSession(new SessionData
            {
                StormReplay = stormReplay,
                ReplayId = replayId,
                GameType = gameType,
                Payloads = payloads,
                Players = players,
                Panels = panels,
                GatesOpen = start,
                CoreKilled = end,
                ReplayData = replayData,
                IsCarriedObjectiveMap = carriedObjectives,
                Loaded = DateTime.Now,
                Timer = null,
                ViewerCancelRequestSource = new CancellationTokenSource()
            });

            var ended = DateTime.Now;
            var duration = ended - started;

            logger.LogInformation($"Time to create session data: {duration}");

            logger.LogInformation($"Session set for: {stormReplay.Path}");
        }
    }
}