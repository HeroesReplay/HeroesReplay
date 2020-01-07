using Heroes.ReplayParser;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace HeroesReplay
{
    /// <summary>
    /// The ViewSpan is a way to filter out information from the replay file that is not of interest within the given timespan.
    /// </summary>
    public class ViewSpan
    {
        public IEnumerable<Unit> Deaths => KilledByPlayers.Concat(KilledByOther);
        public IEnumerable<Unit> MapObjectives => Units.Where(unit => unit.Group == Unit.UnitGroup.MapObjective);
        public IEnumerable<Unit> Structures => Units.Where(unit => unit.Group == Unit.UnitGroup.Structures);
        public IEnumerable<Player> Alive => BlueAlive.Concat(RedAlive);
        public IEnumerable<TimeSpan> Talents => BlueTalentTimes.Concat(RedTalentTimes).Where(timeSpan => timeSpan.WithinViewSpan(Timer, Upper)).OrderBy(timeSpan => timeSpan);
        public IEnumerable<TeamObjective> TeamObjectives => replay.TeamObjectives.SelectMany(teamObjectives => teamObjectives).Where(teamObjective => teamObjective.WithinViewSpan(Timer, Upper) && teamObjective.Player != null).OrderBy(u => u.TimeSpan);

        public TimeSpan Upper { get; }

        private readonly Replay replay;
        private readonly Stopwatch stopWatch;
        
        private static readonly int[] TalentLevels = new[] { 1, 4, 7, 10, 13, 16, 20 };

        public ViewSpan(Stopwatch stopWatch, Game game, TimeSpan upper)
        {
            this.Upper = upper;
            this.replay = game.Replay;
            this.stopWatch = stopWatch;
        }

        private TimeSpan Timer => stopWatch.Elapsed;

        private IEnumerable<Unit> Units => replay.Units.Where(unit => unit.IsWithinViewSpan(Timer, Upper) && unit.HasPlayerAssociated()).OrderBy(unit => unit.TimeSpanDied);
        private IEnumerable<Unit> KilledByPlayers => Units.Where(unit => unit.IsHero() && unit.PlayerKilledBy != null);
        private IEnumerable<Unit> KilledByOther => Units.Where(unit => unit.IsHero() && unit.PlayerKilledBy == null);

        private IEnumerable<Player> BlueAlive => replay.Players.Take(5).Where(player => !player.HeroUnits.Any(unit => unit.IsWithinViewSpan(Timer, Upper)));
        private IEnumerable<Player> RedAlive => replay.Players.Skip(5).Where(player => !player.HeroUnits.Any(unit => unit.IsWithinViewSpan(Timer, Upper)));

        private IEnumerable<TimeSpan> BlueTalentTimes => replay.TeamLevels[0].Where(teamLevel => TalentLevels.Contains(teamLevel.Key)).Select(teamLevel => teamLevel.Value);
        private IEnumerable<TimeSpan> RedTalentTimes => replay.TeamLevels[1].Where(teamLevel => TalentLevels.Contains(teamLevel.Key)).Select(teamLevel => teamLevel.Value);
    }
}