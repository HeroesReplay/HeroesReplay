using Heroes.ReplayParser;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace HeroesReplay
{
    
    /// <summary>
    /// The ViewContext is a way to filter out units from the replay file that are not of interest within the given upper limit timespan.
    /// The GameSpectator can multiple uses depending on what accuracy is needed
    /// </summary>
    public class ViewContext
    {
        public IEnumerable<Unit> Deaths => KilledByPlayers.Concat(KilledByOther); // deaths
        public IEnumerable<Unit> Objectives => Units.Where(unit => unit.Group == Unit.UnitGroup.MapObjective); // objectives
        public IEnumerable<Unit> Structures => Units.Where(unit => unit.Group == Unit.UnitGroup.Structures); // structures	
        public IEnumerable<Player> Alive => BlueAlive.Concat(RedAlive); // alive
        public IEnumerable<TimeSpan> Talents => BlueTalentTimes.Concat(RedTalentTimes).Where(timeSpan => CanRaiseTalentPanel(timeSpan)).OrderBy(timeSpan => timeSpan);
        public IEnumerable<TeamObjective> TeamObjectives => replay.TeamObjectives.SelectMany(teamObjectives => teamObjectives).Where(teamObjective => CanRaiseObjectivePanel(teamObjective)).OrderBy(u => u.TimeSpan);

        public TimeSpan UpperLimit { get; }

        private readonly Replay replay;
        private readonly Stopwatch stopWatch;
        private static readonly int[] TalentLevels = new[] { 1, 4, 7, 10, 13, 16, 20 };

        public ViewContext(Stopwatch stopWatch, Replay replay, TimeSpan upper)
        {
            UpperLimit = upper;
            this.replay = replay;
            this.stopWatch = stopWatch;
        }

        private TimeSpan Timer => stopWatch.Elapsed;
        private IEnumerable<Unit> Units => replay.Units.Where(unit => WillDieWithinTimeFrame(unit)).OrderBy(u => u.TimeSpanDied.Value);

        private IEnumerable<Unit> KilledByPlayers => Units.Where(unit => unit.Group == Unit.UnitGroup.Hero && unit.PlayerKilledBy != null);
        private IEnumerable<Unit> KilledByOther => Units.Where(unit => unit.Group == Unit.UnitGroup.Hero && (unit.PlayerKilledBy == null || unit.PlayerKilledBy == unit.PlayerControlledBy));

        private IEnumerable<Player> BlueAlive => replay.Players.Take(5).Where(player => !player.HeroUnits.Any(unit => WillDieWithinTimeFrame(unit)));
        private IEnumerable<Player> RedAlive => replay.Players.Skip(5).Where(player => !player.HeroUnits.Any(unit => WillDieWithinTimeFrame(unit)));

        private IEnumerable<TimeSpan> BlueTalentTimes => replay.TeamLevels[0].Where(teamLevel => TalentLevels.Contains(teamLevel.Key)).Select(teamLevel => teamLevel.Value);
        private IEnumerable<TimeSpan> RedTalentTimes => replay.TeamLevels[1].Where(teamLevel => TalentLevels.Contains(teamLevel.Key)).Select(teamLevel => teamLevel.Value);

        private bool WithinTimeFrame(TimeSpan value) => value > Timer && value.Subtract(Timer) <= UpperLimit;
        private bool WillDieWithinTimeFrame(Unit unit) => unit.TimeSpanDied.HasValue && WithinTimeFrame(unit.TimeSpanDied.Value);

        private bool CanRaiseObjectivePanel(TeamObjective objective) => WithinTimeFrame(objective.TimeSpan);
        private bool CanRaiseTalentPanel(TimeSpan talentTime) => WithinTimeFrame(talentTime);        
    }
}
