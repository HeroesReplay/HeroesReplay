using System;
using System.Collections.Generic;
using Heroes.ReplayParser;

namespace HeroesReplay.Spectator
{
    public sealed class AnalyzerResult
    {
        public List<Unit> PlayerDeaths { get; }
        public List<Unit> MapObjectives { get; }
        public List<Unit> Structures { get; }
        public List<Player> PlayersAlive { get; }
        public List<(int Team, TimeSpan TalentTime)> Talents { get; }
        public List<TeamObjective> TeamObjectives { get; }
        public TimeSpan Start { get; }
        public TimeSpan End { get; }
        public TimeSpan Duration { get; }
        public StormReplay StormReplay { get; }

        public AnalyzerResult(StormReplay stormReplay, TimeSpan start, TimeSpan end, TimeSpan duration, List<Unit> playerDeaths, List<Unit> mapObjectives, List<Unit> structures, List<Player> playersAlive, List<(int Team, TimeSpan TalentTime)> talents, List<TeamObjective> teamObjectives)
        {
            StormReplay = stormReplay ?? throw new ArgumentNullException(nameof(stormReplay));
            Start = start;
            End = end;
            Duration = duration;
            PlayerDeaths = playerDeaths ?? throw new ArgumentNullException(nameof(playerDeaths));
            MapObjectives = mapObjectives ?? throw new ArgumentNullException(nameof(mapObjectives));
            Structures = structures ?? throw new ArgumentNullException(nameof(structures));
            PlayersAlive = playersAlive ?? throw new ArgumentNullException(nameof(playersAlive));
            Talents = talents ?? throw new ArgumentNullException(nameof(talents));
            TeamObjectives = teamObjectives ?? throw new ArgumentNullException(nameof(teamObjectives));
        }
    }
}