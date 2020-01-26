using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Shared;

namespace HeroesReplay.Analyzer
{
    public sealed class AnalyzerResult
    {
        public IEnumerable<Unit> PlayerDeaths { get; }
        public IEnumerable<Unit> MapObjectives { get; }
        public IEnumerable<Unit> Structures { get; }
        public IEnumerable<Player> PlayersAlive { get; }
        public IEnumerable<(int Team, TimeSpan TalentTime)> Talents { get; }
        public IEnumerable<TeamObjective> TeamObjectives { get; }

        /// <summary>
        /// Ping events are only from the team which the replay file originates from
        /// </summary>
        public IEnumerable<GameEvent> PingSources { get; }
        public IEnumerable<Player> PreviousKillers { get;  }
        public IEnumerable<(Player, TimeSpan)> Camps { get; }
        public TimeSpan Start { get; }
        public TimeSpan End { get; }
        public TimeSpan Duration { get; }
        public StormReplay StormReplay { get; }

        public AnalyzerResult(
            StormReplay stormReplay, 
            TimeSpan start,
            TimeSpan end,
            TimeSpan duration,
            IEnumerable<Unit> playerDeaths,
            IEnumerable<Unit> mapObjectives,
            IEnumerable<Unit> structures,
            IEnumerable<Player> playersAlive,
            IEnumerable<Player> killers,
            IEnumerable<GameEvent> pingSources,
            IEnumerable<(int Team, TimeSpan TalentTime)> talents,
            IEnumerable<TeamObjective> teamObjectives,
            IEnumerable<(Player, TimeSpan)> camps)
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