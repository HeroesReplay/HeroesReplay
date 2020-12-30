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
        private readonly ISessionSetter sessionSetter;
        private readonly ILogger<SessionCreator> logger;

        public SessionCreator(ILogger<SessionCreator> logger, IReplayAnalzer replayAnalyzer, ISessionSetter sessionSetter)
        {
            this.replayAnalyzer = replayAnalyzer;
            this.sessionSetter = sessionSetter;
            this.logger = logger;
        }

        public async Task CreateAsync(StormReplay stormReplay)
        {
            DateTime start = DateTime.Now;

            logger.LogInformation($"Creating session for: {stormReplay.Path}");

            var players = replayAnalyzer.GetPlayers(stormReplay.Replay);
            var panels = replayAnalyzer.GetPanels(stormReplay.Replay);
            var end = replayAnalyzer.GetEnd(stormReplay.Replay);
            var isCarriedObjectiveMap = replayAnalyzer.IsCarriedObjectiveMap(stormReplay.Replay);

            sessionSetter.Set(new SessionData(players, panels, end, isCarriedObjectiveMap), stormReplay);

            logger.LogDebug($"Time to create session data: {DateTime.Now - start}");

            logger.LogInformation($"Session set for: {stormReplay.Path}");
        }
    }
}