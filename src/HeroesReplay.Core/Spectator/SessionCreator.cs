using HeroesReplay.Core.Models;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using System;

namespace HeroesReplay.Core
{
    public class SessionCreator : ISessionCreator
    {
        private readonly IReplayAnalzer replayAnalyzer;
        private readonly ISessionSetter sessionSetter;
        private readonly ILogger<SessionCreator> logger;

        public SessionCreator(ILogger<SessionCreator> logger, IReplayAnalzer replayAnalyzer, ISessionSetter sessionSetter)
        {
            this.replayAnalyzer = replayAnalyzer ?? throw new ArgumentNullException(nameof(replayAnalyzer));
            this.sessionSetter = sessionSetter ?? throw new ArgumentNullException(nameof(sessionSetter));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Create(StormReplay stormReplay)
        {
            if (stormReplay == null)
                throw new ArgumentNullException(nameof(stormReplay));

            DateTime start = DateTime.Now;

            logger.LogInformation($"Creating session for: {stormReplay.Path}");

            var players = replayAnalyzer.GetPlayers(stormReplay.Replay);
            var panels = replayAnalyzer.GetPanels(stormReplay.Replay);
            var end = replayAnalyzer.GetEnd(stormReplay.Replay);
            var isCarriedObjectiveMap = replayAnalyzer.IsCarriedObjectiveMap(stormReplay.Replay);

            sessionSetter.SetSession(new SessionData(players, panels, end, isCarriedObjectiveMap), stormReplay);

            logger.LogDebug($"Time to create session data: {DateTime.Now - start}");

            logger.LogInformation($"Session set for: {stormReplay.Path}");
        }
    }
}