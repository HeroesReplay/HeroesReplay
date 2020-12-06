using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class PlayerTauntingCalculator : IFocusCalculator
    {
        private readonly Settings settings;
        private readonly IAbilityDetector abilityDetector;

        public PlayerTauntingCalculator(Settings settings, IAbilityDetector abilityDetector)
        {
            this.settings = settings;
            this.abilityDetector = abilityDetector;
        }

        // https://github.com/ebshimizu/hots-parser/blob/master/parser.js#L2185
        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            IEnumerable<GameEvent> gameEvents = replay.GameEvents.Where(e => e.TimeSpan == now && e.eventType == GameEventType.CCmdEvent);

            foreach (IGrouping<Player, GameEvent> events in gameEvents.Where(e => abilityDetector.IsAbility(replay, e, settings.AbilityDetection.Hearth)).GroupBy(e => e.player))
            {
                var bsteps = events.GroupBy(cmd => cmd.TimeSpan).Where(g => g.Count() > 3);

                if (bsteps.Any())
                {
                    var bstepCount = bsteps.Max(x => x.Key);
                    yield return new Focus(this, events.Key.HeroUnits.FirstOrDefault(u => u.Positions.Any(p => p.TimeSpan == now)), events.Key, settings.Weights.TauntingBStep, $"{events.Key.HeroId} bstepping");
                }
            }

            foreach (IGrouping<Player, GameEvent> events in gameEvents.Where(e => abilityDetector.IsAbility(replay, e, settings.AbilityDetection.Taunt)).GroupBy(e => e.player))
            {
                yield return new Focus(this, events.Key.HeroUnits.FirstOrDefault(u => u.Positions.Any(p => p.TimeSpan == now)), events.Key, settings.Weights.TauntingEmote, $"{events.Key.HeroId} taunting");
            }

            foreach (IGrouping<Player, GameEvent> events in gameEvents.Where(e => abilityDetector.IsAbility(replay, e, settings.AbilityDetection.Dance)).GroupBy(e => e.player))
            {
                yield return new Focus(this, events.Key.HeroUnits.FirstOrDefault(u => u.Positions.Any(p => p.TimeSpan == now)), events.Key, settings.Weights.TauntingEmote, $"{events.Key.HeroId} dancing");
            }
        }


    }
}