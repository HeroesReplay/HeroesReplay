using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class NearEnemyCalculator : IFocusCalculator
    {
        private readonly AppSettings settings;

        public NearEnemyCalculator(AppSettings settings)
        {
            this.settings = settings;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null) 
                throw new ArgumentNullException(nameof(replay));

            List<IGrouping<int, Unit>> teams = replay.Players
                .SelectMany(x => x.HeroUnits)
                .Where(unit => unit.Team != null && unit.TimeSpanBorn < now && unit.TimeSpanDied > now)
                .GroupBy(x => x.Team.GetValueOrDefault()).ToList();

            if (teams.Count != 2) yield break;

            var units = new List<(Unit teamOneUnit, Unit teamTwoUnit, double Distance)>();

            foreach (var teamOneUnit in teams[0])
            {
                foreach (var teamTwoUnit in teams[1])
                {
                    foreach (var teamTwoPos in teamTwoUnit.Positions.Where(p => p.TimeSpan == now))
                    {
                        foreach (var teamOnePos in teamOneUnit.Positions.Where(p => p.TimeSpan == now))
                        {
                            var distance = teamTwoPos.Point.DistanceTo(teamOnePos.Point);

                            if (distance <= settings.Spectate.MaxDistanceToEnemy)
                            {
                                units.Add((teamOneUnit, teamTwoUnit, distance));
                            }
                        }
                    }
                }
            }

            var pair = units.OrderBy(x => x.Distance).FirstOrDefault();

            if (pair.teamOneUnit != null && pair.teamTwoUnit != null)
            {
                var heroes = new[] { pair.teamOneUnit, pair.teamTwoUnit };
                Unit target = heroes.OrderBy(x => Guid.NewGuid()).FirstOrDefault();
                Unit enemy = heroes.Except(new[] { target }).FirstOrDefault();

                float prioritiseCloserHero = Convert.ToSingle(pair.Distance) / settings.Weights.NearEnemyHeroDistanceDivisor;

                yield return new Focus(
                    GetType(), 
                    target, 
                    target.PlayerControlledBy,
                    settings.Weights.NearEnemyHero + settings.Weights.NearEnemyHeroOffset - prioritiseCloserHero,
                    $"{target.PlayerControlledBy.Character} is in proximity of {enemy.PlayerControlledBy.Character} ({pair.Distance})");
            }
        }
    }
}