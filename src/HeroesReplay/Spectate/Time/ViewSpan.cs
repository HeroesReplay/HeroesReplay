using Heroes.ReplayParser;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace HeroesReplay
{
    /// <summary>
    /// The ViewContext is a way to filter out information from the replay file that are not of interest within the given upper limit timespan.
    /// </summary>
    /// <remarks>
    /// Several ViewContext's could be used to build up more sophisticated logic for the spectator.
    /// </remarks>
    public class ViewSpan
    {
        public IEnumerable<Unit> Deaths => KilledByPlayers.Concat(KilledByOther);
        public IEnumerable<Unit> MapObjectives => Units.Where(unit => unit.Group == Unit.UnitGroup.MapObjective);
        public IEnumerable<Unit> Structures => Units.Where(unit => unit.Group == Unit.UnitGroup.Structures);
        public IEnumerable<Player> Alive => BlueAlive.Concat(RedAlive);
        public IEnumerable<TimeSpan> Talents => BlueTalentTimes.Concat(RedTalentTimes).Where(timeSpan => timeSpan.WithinViewSpan(Timer, Upper)).OrderBy(timeSpan => timeSpan);
        public IEnumerable<TeamObjective> TeamObjectives => replay.TeamObjectives.SelectMany(teamObjectives => teamObjectives).Where(teamObjective => teamObjective.WithinViewSpan(Timer, Upper)).OrderBy(u => u.TimeSpan);

        public TimeSpan Upper { get; }

        private readonly Replay replay;
        private readonly Stopwatch stopWatch;
        private static readonly int[] TalentLevels = new[] { 1, 4, 7, 10, 13, 16, 20 };

        public ViewSpan(Stopwatch stopWatch, Replay replay, TimeSpan upper)
        {
            Upper = upper;
            this.replay = replay;
            this.stopWatch = stopWatch;
        }

        private TimeSpan Timer => stopWatch.Elapsed;

        private IEnumerable<Unit> Units => replay.Units.Where(unit => unit.WithinViewSpan(Timer, Upper));
        private IEnumerable<Unit> KilledByPlayers => Units.Where(unit => unit.WillDie()).Where(unit => unit.Group == Unit.UnitGroup.Hero && unit.PlayerKilledBy != null);
        private IEnumerable<Unit> KilledByOther => Units.Where(unit => unit.WillDie()).Where(unit => unit.Group == Unit.UnitGroup.Hero && (unit.PlayerKilledBy == null || unit.PlayerKilledBy == unit.PlayerControlledBy));

        private IEnumerable<Player> BlueAlive => replay.Players.Take(5).Where(player => !player.HeroUnits.Any(unit => unit.WithinViewSpan(Timer, Upper)));
        private IEnumerable<Player> RedAlive => replay.Players.Skip(5).Where(player => !player.HeroUnits.Any(unit => unit.WithinViewSpan(Timer, Upper)));

        private IEnumerable<TimeSpan> BlueTalentTimes => replay.TeamLevels[0].Where(teamLevel => TalentLevels.Contains(teamLevel.Key)).Select(teamLevel => teamLevel.Value);
        private IEnumerable<TimeSpan> RedTalentTimes => replay.TeamLevels[1].Where(teamLevel => TalentLevels.Contains(teamLevel.Key)).Select(teamLevel => teamLevel.Value);
    }
}