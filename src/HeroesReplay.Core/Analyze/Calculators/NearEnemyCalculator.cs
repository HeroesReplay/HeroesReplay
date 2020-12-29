using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class NearEnemyCalculator : IFocusCalculator
    {
        private readonly Settings settings;

        public NearEnemyCalculator(Settings settings)
        {
            this.settings = settings;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            List<IGrouping<int, Unit>> teams = replay.Players.SelectMany(x => x.HeroUnits).Where(unit => unit.Team != null && unit.TimeSpanBorn < now && unit.TimeSpanDied > now).GroupBy(x => x.Team.Value).ToList();

            if (teams.Count != 2) yield break;

            foreach (var teamOneUnit in teams[0])
            {
                foreach (var teamTwoUnit in teams[1])
                {
                    foreach (var teamTwoPos in teamTwoUnit.Positions.Where(p => p.TimeSpan == now))
                    {
                        foreach (var teamOnePos in teamOneUnit.Positions.Where(p => p.TimeSpan == now))
                        {
                            var distance = teamTwoPos.Point.DistanceTo(teamOnePos.Point);

                            if (distance < settings.Spectate.MaxDistanceToEnemy)
                            {
                                var unit = new[] { teamOneUnit, teamTwoUnit }.OrderBy(x => Guid.NewGuid()).First();

                                yield return new Focus(GetType(), unit, unit.PlayerControlledBy, settings.Weights.NearEnemyHero, $"{teamOneUnit.PlayerControlledBy.HeroId} is in proximity of {teamTwoUnit.PlayerControlledBy.HeroId}");
                            }
                        }
                    }
                }
            }
        }
    }
}