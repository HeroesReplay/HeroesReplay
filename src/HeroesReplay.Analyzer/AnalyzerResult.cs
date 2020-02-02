﻿using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Shared;

namespace HeroesReplay.Analyzer
{
    public sealed class AnalyzerResult
    {
        public IEnumerable<Unit> Deaths { get; }
        public IEnumerable<Unit> MapObjectives { get; }
        public IEnumerable<Unit> Structures { get; }
        public IEnumerable<Player> Alive { get; }
        public IEnumerable<Player> NearSpawn { get; }
        public IEnumerable<Player> Proximity { get; }

        public IEnumerable<(int, TimeSpan)> Talents { get; }
        public IEnumerable<TeamObjective> TeamObjectives { get; }

        /// <summary>
        /// Ping events are only from the team which the replay file originates from
        /// </summary>
        public IEnumerable<GameEvent> Pings { get; }
        public IEnumerable<Player> Killers { get; }

        /// <summary>
        /// Standard camps are not captured in TeamObjectives
        /// </summary>
        /// <remarks>
        /// https://github.com/barrett777/Heroes.ReplayParser/blob/2d29bf2f66bfd44c471a4214698e6b517d38ecd3/Heroes.ReplayParser/Statistics.cs#L343
        /// </remarks>
        public IEnumerable<(Player, TimeSpan)> CampCaptures { get; }
        public IEnumerable<(Player, TimeSpan)> Mercenaries { get; }

        public TimeSpan Start { get; }
        public TimeSpan End { get; }
        public TimeSpan Duration { get; }
        public StormReplay StormReplay { get; }

        public AnalyzerResult(
            StormReplay stormReplay,
            TimeSpan start,
            TimeSpan end,
            TimeSpan duration,
            IEnumerable<Unit> deaths,
            IEnumerable<Unit> mapObjectives,
            IEnumerable<Unit> structures,
            IEnumerable<Player> alive,
            IEnumerable<Player> nearSpawn,
            IEnumerable<Player> proximity,
            IEnumerable<Player> killers,
            IEnumerable<GameEvent> pings,
            IEnumerable<(int, TimeSpan)> talents,
            IEnumerable<TeamObjective> teamObjectives,
            IEnumerable<(Player, TimeSpan)> campCaptures,
            IEnumerable<(Player, TimeSpan)> mercenaries)
        {
            Start = start;
            End = end;
            Duration = duration;

            StormReplay = stormReplay ?? throw new ArgumentNullException(nameof(stormReplay));
            Killers = killers ?? throw new ArgumentNullException(nameof(killers));
            CampCaptures = campCaptures ?? throw new ArgumentNullException(nameof(campCaptures));
            Mercenaries = mercenaries ?? throw new ArgumentNullException(nameof(mercenaries));
            Pings = pings ?? throw new ArgumentNullException(nameof(pings));
            Deaths = deaths ?? throw new ArgumentNullException(nameof(deaths));
            MapObjectives = mapObjectives ?? throw new ArgumentNullException(nameof(mapObjectives));
            Structures = structures ?? throw new ArgumentNullException(nameof(structures));
            Alive = alive ?? throw new ArgumentNullException(nameof(alive));
            NearSpawn = nearSpawn ?? throw new ArgumentNullException(nameof(nearSpawn));
            Proximity = proximity ?? throw new ArgumentNullException(nameof(proximity));
            Talents = talents ?? throw new ArgumentNullException(nameof(talents));
            TeamObjectives = teamObjectives ?? throw new ArgumentNullException(nameof(teamObjectives));
        }
    }
}