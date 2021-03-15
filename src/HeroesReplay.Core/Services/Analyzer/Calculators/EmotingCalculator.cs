using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Analyzer.Calculators
{
    public class EmotingCalculator : IFocusCalculator
    {
        private readonly IGameData gameData;
        private readonly IAbilityDetector abilityDetector;
        private readonly WeightOptions weightOptions;
        private readonly SpectateOptions spectateOptions;
        private readonly AbilityDetectionOptions abilityOptions;

        public EmotingCalculator(
            IOptions<WeightOptions> weightOptions, 
            IOptions<SpectateOptions> spectateOptions,
            IOptions<AbilityDetectionOptions> abilityOptions,
            IGameData gameData, 
            IAbilityDetector abilityDetector)
        {
            this.gameData = gameData;
            this.weightOptions = weightOptions.Value;
            this.abilityOptions = abilityOptions.Value;
            this.spectateOptions = spectateOptions.Value;
            this.abilityDetector = abilityDetector;
        }

        // https://github.com/ebshimizu/hots-parser/blob/master/parser.js#L2185
        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            IEnumerable<GameEvent> gameEvents = replay.GameEvents.Where(e => e.TimeSpan == now && e.eventType == GameEventType.CCmdEvent);

            foreach (IGrouping<Player, GameEvent> events in gameEvents.Where(e => abilityDetector.IsAbility(replay, e, abilityOptions.Hearth)).GroupBy(e => e.player))
            {
                var bsteps = events.GroupBy(cmd => cmd.TimeSpan).Where(g => g.Count() > 3);

                if (bsteps.Any())
                {
                    var bstepCount = bsteps.Max(x => x.Key);

                    yield return new Focus(
                        GetType(),
                        events.Key.HeroUnits.FirstOrDefault(u => u.Positions.Any(p => p.TimeSpan == now)),
                        events.Key,
                        weightOptions.BStep,
                        $"{events.Key.Character} bstepping");
                }
            }

            foreach (IGrouping<Player, GameEvent> events in gameEvents.Where(e => abilityDetector.IsAbility(replay, e, abilityOptions.Taunt)).GroupBy(e => e.player))
            {
                yield return new Focus(
                    GetType(),
                    events.Key.HeroUnits.FirstOrDefault(u => u.Positions.Any(p => p.TimeSpan == now)),
                    events.Key,
                    weightOptions.Taunt,
                    $"{events.Key.Character} taunting");
            }

            foreach (IGrouping<Player, GameEvent> events in gameEvents.Where(e => abilityDetector.IsAbility(replay, e, abilityOptions.Dance)).GroupBy(e => e.player))
            {
                yield return new Focus(
                    GetType(), 
                    events.Key.HeroUnits.FirstOrDefault(u => u.Positions.Any(p => p.TimeSpan == now)),
                    events.Key,
                    weightOptions.Dance,
                    $"{events.Key.Character} dancing");
            }
        }
    }
}