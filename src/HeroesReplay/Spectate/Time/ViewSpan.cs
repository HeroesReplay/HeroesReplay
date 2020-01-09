using Heroes.ReplayParser;
using System;
using System.Linq;

namespace HeroesReplay
{
    /// <summary>
    /// The ViewSpan is a way to filter out information from the replay file that is not of interest within the given timespan.
    /// </summary>
    public class ViewSpan : IDisposable
    {
        public Unit[] Deaths { get; }
        public Unit[] MapObjectives { get; }
        public Unit[] Structures { get; }
        public Player[] Alive { get; }
        public (int Team, TimeSpan TalentTime)[] Talents { get; }
        public TeamObjective[] TeamObjectives { get; }

        public TimeSpan End { get; }
        public TimeSpan Start { get; }
        public TimeSpan Range => End - Start;

        private static readonly int[] TalentLevels = new[] { 1, 4, 7, 10, 13, 16, 20 };
        private Unit[] Units { get; set; }
        private Replay Replay { get; }

        public ViewSpan(Replay replay, TimeSpan start, TimeSpan end)
        {
            this.End = end;
            this.Start = start;
            this.Replay = replay;

            Units = replay.Units.Where(unit =>
                unit.IsWithin(start, end) &&  // this unit dies (so we know it's interesting
                unit.IsPlayerReferenced() &&  // a player is somehow linked, which we can focus
                    (unit.IsMapObjective() ||  // its a map objective
                    unit.IsStructure() || // or a structure
                    unit.IsCamp() || // is a camp
                    unit.IsHero()) // is controlled by a hero
                 ).ToArray();

            this.Deaths = Units.Where(unit => unit.IsHero()).OrderByDeath().ToArray();
            this.MapObjectives = Units.Where(unit => unit.IsMapObjective()).OrderByDeath().ToArray();
            this.Structures = Units.Where(unit => unit.IsStructure()).OrderByDeath().ToArray();

            this.Alive = Replay.Players
                .Where(player => !player.HeroUnits.Any(unit => unit.IsWithin(Start, End))).ToArray();

            this.Talents = Replay.TeamLevels
                .SelectMany(teamLevels => teamLevels)
                .Where(teamLevel => TalentLevels.Contains(teamLevel.Key))
                .Where(teamLevel => teamLevel.Value.IsWithin(Start, End))
                .Select(x => (Team: x.Key, TalentTime: x.Value))
                .OrderBy(team => team.TalentTime).ToArray();
            
            this.TeamObjectives = Replay.TeamObjectives
                .SelectMany(teamObjectives => teamObjectives)
                .Where(teamObjective => teamObjective.TimeSpan.IsWithin(start, end) && teamObjective.Player != null)
                .OrderBy(objective => objective.TimeSpan).ToArray();
        }

        public void Dispose()
        {

        }
    }
}