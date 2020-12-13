using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using System;
using System.Linq;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public class SessionCreator : ISessionCreator
    {
        private IReplayAnalzer replayAnalyzer;
        private readonly ISessionSetter sessionWriter;
        private readonly ILogger<SessionCreator> logger;

        public SessionCreator(ILogger<SessionCreator> logger, IReplayAnalzer replayAnalyzer, ISessionSetter sessionSetter)
        {
            this.replayAnalyzer = replayAnalyzer;
            this.sessionWriter = sessionSetter;
            this.logger = logger;
        }

        public async Task SetSessionAsync(StormReplay stormReplay)
        {
            logger.LogInformation($"Creating session for: {stormReplay.Path}");

            var players = replayAnalyzer.GetPlayers(stormReplay.Replay);
            var panels = replayAnalyzer.GetPanels(stormReplay.Replay);
            var end = replayAnalyzer.GetEnd(stormReplay.Replay);
            var isCarriedObjectiveMap = replayAnalyzer.IsCarriedObjectiveMap(stormReplay.Replay);

            sessionWriter.Set(new SessionData(players, panels, end, isCarriedObjectiveMap), stormReplay);

            logger.LogInformation($"Session set for: {stormReplay.Path}");
        }
    }
}