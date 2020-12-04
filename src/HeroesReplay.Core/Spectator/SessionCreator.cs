using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using System;
using System.Linq;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public class SessionCreator : ISessionCreator
    {
        private IReplayAnalzer replayAnalyzer;
        private readonly ISessionWriter sessionWriter;
        private readonly ILogger<SessionCreator> logger;
        private IConfiguration configuration;

        public SessionCreator(ILogger<SessionCreator> logger, IConfiguration configuration, IReplayAnalzer replayAnalyzer, ISessionWriter sessionWriter)
        {
            this.replayAnalyzer = replayAnalyzer;
            this.sessionWriter = sessionWriter;
            this.configuration = configuration;
            this.logger = logger;
        }

        public async Task SetSessionAsync(StormReplay stormReplay)
        {
            logger.LogInformation($"Creating session for: {stormReplay.Path}");

            var maps = configuration.GetSection("Settings:CarriedObjectiveMaps").Get<string[]>();
            var cores = configuration.GetSection("Settings:CoreUnitNames").Get<string[]>();
            var endScreenTime = configuration.GetValue<TimeSpan>("Settings:EndScreenTime");

            var players = replayAnalyzer.GetPlayers(stormReplay.Replay);
            var panels = replayAnalyzer.GetPanels(stormReplay.Replay);
            var end = stormReplay.Replay.Units.Where(unit => cores.Contains(unit.Name) && unit.TimeSpanDied.HasValue).Min(core => core.TimeSpanDied.Value).Add(endScreenTime);
            var isCarriedObjectiveMap = maps.Contains(stormReplay.Replay.Map);

            var sessionData = new SessionData(players, panels, end, isCarriedObjectiveMap);

            sessionWriter.SessionData = sessionData;
            sessionWriter.StormReplay = stormReplay;
        }
    }
}