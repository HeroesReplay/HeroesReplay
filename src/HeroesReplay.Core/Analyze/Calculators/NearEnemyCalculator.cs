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
            foreach (var heroUnit0 in replay.Units.Where(heroUnit0 => heroUnit0.Name.StartsWith("Hero") && heroUnit0.Team == 0 && heroUnit0.TimeSpanBorn < now && heroUnit0.TimeSpanDied > now))
            {
                foreach (var heroUnit1 in replay.Units.Where(heroUnit1 => heroUnit1.Name.StartsWith("Hero") && heroUnit1.Team == 1 && heroUnit1.TimeSpanBorn < now && heroUnit1.TimeSpanDied > now))
                {
                    foreach (var position1 in heroUnit1.Positions.Where(p => p.TimeSpan == now))
                    {
                        foreach (var position0 in heroUnit0.Positions.Where(p => p.TimeSpan == now))
                        {
                            var distance = position1.Point.DistanceTo(position0.Point);

                            if (distance <= 10)
                            {
                                yield return new Focus(this, heroUnit0, heroUnit0.PlayerControlledBy, settings.Weights.NearEnemyHero, $"{heroUnit0.PlayerControlledBy.HeroId} is in proximity of {heroUnit1.PlayerControlledBy.HeroId}");
                            }
                        }
                    }
                }
            }
        }
    }
}