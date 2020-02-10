using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;

namespace HeroesReplay.Core.Analyzer
{
    public sealed class AnalyzerResult
    {
        /// <summary>
        /// Players that will die in this time frame.
        /// </summary>
        public IEnumerable<Unit> Deaths { get; }

        /// <summary>
        /// Objectives can range from owner change events, to units dying
        /// </summary>
        public IEnumerable<(Player, TimeSpan)> MapObjectives { get; }

        /// <summary>
        /// Structures that are destoryed in this time frame.
        /// </summary>
        public IEnumerable<Unit> Structures { get; }

        /// <summary>
        /// Players that are alive in this time frame.
        /// </summary>
        public IEnumerable<Player> Alive { get; }

        /// <summary>
        /// Players that are close to their spawn/core in this time frame.
        /// </summary>
        public IEnumerable<Player> AllyCore { get; }

        /// <summary>
        /// Players that are close to the enemy spawn/core in this time frame.
        /// </summary>
        public IEnumerable<Player> EnemyCore { get; }

        /// <summary>
        /// Players that are close to enemy players in this time frame.
        /// </summary>
        public IEnumerable<Player> Proximity { get; }

        /// <summary>
        /// Players that bstep, taunt or use dance
        /// </summary>
        public IEnumerable<(Player, TimeSpan)> Taunts { get; }

        /// <summary>
        /// Team talents that are aquired in this time frame.
        /// </summary>
        public IEnumerable<(int, TimeSpan)> TeamTalents { get; }


        /// <summary>
        /// TeamObjectives in this time frame. This includes a number of things, such as Map Objectives or Boss Camps 
        /// </summary>
        public IEnumerable<TeamObjective> TeamObjectives { get; }

        /// <summary>
        /// Player pings that are from the Team from which the replay file originated from in this time frame.
        /// </summary>
        /// <remarks>
        /// This does not include pings from the enemy team. Only pings from the person's team who owns the replay.
        /// </remarks>
        public IEnumerable<GameEvent> Pings { get; }

        /// <summary>
        /// Players who kill a hero in this time frame.
        /// </summary>
        public IEnumerable<(Player, TimeSpan)> Killers { get; }

        /// <summary>
        /// Players who capture a normal camp in this time frame.
        /// </summary>
        public IEnumerable<(Player, TimeSpan)> CampCaptures { get; }

        /// <summary>
        /// Players who capture a Boss camp in this time frame.
        /// </summary>
        public IEnumerable<(Player, TimeSpan)> BossCaptures { get; }

        /// <summary>
        /// Players who destroy enemy units that are not minions or hero abilities.
        /// This should include enemy boss camp and mercenary units
        /// </summary>
        public IEnumerable<(Player, TimeSpan)> EnemyUnits { get; }

        public TimeSpan Start { get; }
        public TimeSpan End { get; }
        public TimeSpan Duration { get; }
        public Replay Replay { get; }

        public AnalyzerResult(
            Replay replay,
            TimeSpan start,
            TimeSpan end,
            TimeSpan duration,
            IEnumerable<Unit> deaths,
            IEnumerable<(Player, TimeSpan)> mapObjectives,
            IEnumerable<Unit> structures,
            IEnumerable<Player> alive,
            IEnumerable<Player> allyCore,
            IEnumerable<Player> enemyCore,
            IEnumerable<Player> proximity,
            IEnumerable<(Player, TimeSpan)> taunts,
            IEnumerable<(Player, TimeSpan)> killers,
            IEnumerable<GameEvent> pings,
            IEnumerable<(int, TimeSpan)> teamTalents,
            IEnumerable<TeamObjective> teamObjectives,
            IEnumerable<(Player, TimeSpan)> bossCaptures,
            IEnumerable<(Player, TimeSpan)> campCaptures,
            IEnumerable<(Player, TimeSpan)> enemyUnits)
        {
            Start = start;
            End = end;
            Duration = duration;

            Replay = replay ?? throw new ArgumentNullException(nameof(replay));
            BossCaptures = bossCaptures ?? throw new ArgumentNullException(nameof(bossCaptures));
            CampCaptures = campCaptures ?? throw new ArgumentNullException(nameof(campCaptures));
            EnemyUnits = enemyUnits ?? throw new ArgumentNullException(nameof(enemyUnits));
            Pings = pings ?? throw new ArgumentNullException(nameof(pings));
            Deaths = deaths ?? throw new ArgumentNullException(nameof(deaths));
            MapObjectives = mapObjectives ?? throw new ArgumentNullException(nameof(mapObjectives));
            Structures = structures ?? throw new ArgumentNullException(nameof(structures));
            AllyCore = allyCore ?? throw new ArgumentNullException(nameof(allyCore));
            EnemyCore = enemyCore ?? throw new ArgumentNullException(nameof(enemyCore));
            Taunts = taunts ?? throw new ArgumentNullException(nameof(taunts));
            Proximity = proximity ?? throw new ArgumentNullException(nameof(proximity));
            TeamTalents = teamTalents ?? throw new ArgumentNullException(nameof(teamTalents));
            TeamObjectives = teamObjectives ?? throw new ArgumentNullException(nameof(teamObjectives));
            Killers = killers ?? throw new ArgumentNullException(nameof(killers));
            Alive = alive ?? throw new ArgumentNullException(nameof(alive));
        }
    }
}