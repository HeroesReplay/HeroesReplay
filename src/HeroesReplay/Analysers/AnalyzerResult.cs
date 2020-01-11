using System;
using System.Collections.Generic;
using Heroes.ReplayParser;

namespace HeroesReplay
{
    public sealed class AnalyzerResult
    {
        public List<Unit> Deaths { get; }
        public List<Unit> MapObjectives { get; }
        public List<Unit> Structures { get; }
        public List<Player> Alive { get; }
        public List<(int Team, TimeSpan TalentTime)> Talents { get; }
        public List<TeamObjective> TeamObjectives { get; }
        public TimeSpan Start { get; }
        public TimeSpan End { get; }
        public TimeSpan Range => End - Start;
        public StormReplay StormReplay { get; }

        public AnalyzerResult(StormReplay stormReplay, TimeSpan start, TimeSpan end, List<Unit> deaths, List<Unit> mapObjectives, List<Unit> structures, List<Player> alive, List<(int Team, TimeSpan TalentTime)> talents, List<TeamObjective> teamObjectives)
        {
            StormReplay = stormReplay ?? throw new ArgumentNullException(nameof(stormReplay));
            Start = start;
            End = end;

            Deaths = deaths ?? throw new ArgumentNullException(nameof(deaths));
            MapObjectives = mapObjectives ?? throw new ArgumentNullException(nameof(mapObjectives));
            Structures = structures ?? throw new ArgumentNullException(nameof(structures));
            Alive = alive ?? throw new ArgumentNullException(nameof(alive));
            Talents = talents ?? throw new ArgumentNullException(nameof(talents));
            TeamObjectives = teamObjectives ?? throw new ArgumentNullException(nameof(teamObjectives));
        }
    }
}