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
        private readonly Settings settings;

        public SessionCreator(ILogger<SessionCreator> logger, Settings settings, IReplayAnalzer replayAnalyzer, ISessionSetter sessionSetter)
        {
            this.replayAnalyzer = replayAnalyzer;
            this.sessionWriter = sessionSetter;
            this.logger = logger;
            this.settings = settings;
        }

        public async Task SetSessionAsync(StormReplay stormReplay)
        {
            logger.LogInformation($"Creating session for: {stormReplay.Path}");

            var players = replayAnalyzer.GetPlayers(stormReplay.Replay);
            var panels = replayAnalyzer.GetPanels(stormReplay.Replay);
            var end = stormReplay.Replay.Units.Where(unit => settings.UnitSettings.CoreNames.Contains(unit.Name) && unit.TimeSpanDied.HasValue).Min(core => core.TimeSpanDied.Value).Add(settings.SpectateSettings.EndScreenTime);
            var isCarriedObjectiveMap = settings.MapSettings.CarriedObjectives.Contains(stormReplay.Replay.Map);
            var sessionData = new SessionData(players, panels, end, isCarriedObjectiveMap);

            sessionWriter.Set(sessionData, stormReplay);
        }
    }
}