using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Shared;

namespace HeroesReplay.Analyzer
{
    public sealed class AnalyzerResult
    {
        public List<Unit> PlayerDeaths { get; }
        public List<Unit> MapObjectives { get; }
        public List<Unit> Structures { get; }
        public List<Player> PlayersAlive { get; }
        public List<(int Team, TimeSpan TalentTime)> Talents { get; }
        public List<TeamObjective> TeamObjectives { get; }

        /// <summary>
        /// Ping events are only from the team which the replay file originates from
        /// </summary>
        public List<GameEvent> PingSources { get; }
        public List<Player> PreviousKillers { get;  }
        public List<(Player, TimeSpan)> Camps { get; }
        public TimeSpan Start { get; }
        public TimeSpan End { get; }
        public TimeSpan Duration { get; }
        public StormReplay StormReplay { get; }

        public AnalyzerResult(
            StormReplay stormReplay, 
            TimeSpan start,
            TimeSpan end,
            TimeSpan duration,
            List<Unit> playerDeaths, 
            List<Unit> mapObjectives,
            List<Unit> structures,
            List<Player> playersAlive,
            List<Player> killers,
            List<GameEvent> pingSources,
            List<(int Team, TimeSpan TalentTime)> talents, 
            List<TeamObjective> teamObjectives,
            List<(Player, TimeSpan)> camps)
        {
            Start = start;
            End = end;
            Duration = duration;

            StormReplay = stormReplay ?? throw new ArgumentNullException(nameof(stormReplay));
            PreviousKillers = killers ?? throw new ArgumentNullException(nameof(killers));
            Camps = camps ?? throw new ArgumentNullException(nameof(camps));
            PingSources = pingSources ?? throw new ArgumentNullException(nameof(pingSources));
            PlayerDeaths = playerDeaths ?? throw new ArgumentNullException(nameof(playerDeaths));
            MapObjectives = mapObjectives ?? throw new ArgumentNullException(nameof(mapObjectives));
            Structures = structures ?? throw new ArgumentNullException(nameof(structures));
            PlayersAlive = playersAlive ?? throw new ArgumentNullException(nameof(playersAlive));
            Talents = talents ?? throw new ArgumentNullException(nameof(talents));
            TeamObjectives = teamObjectives ?? throw new ArgumentNullException(nameof(teamObjectives));
        }
    }
}