﻿using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Analysis.Calculators
{
    public class EmotingCalculator : IFocusCalculator
    {
        private readonly AppSettings settings;
        private readonly IAbilityDetector abilityDetector;

        public EmotingCalculator(AppSettings settings, IAbilityDetector abilityDetector)
        {
            this.settings = settings;
            this.abilityDetector = abilityDetector;
        }

        // https://github.com/ebshimizu/hots-parser/blob/master/parser.js#L2185
        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            IEnumerable<GameEvent> gameEvents = replay.GameEvents.Where(e => e.TimeSpan == now && e.eventType == GameEventType.CCmdEvent);

            foreach (IGrouping<Player, GameEvent> events in gameEvents.Where(e => abilityDetector.IsAbility(replay, e, settings.AbilityDetection.Hearth)).GroupBy(e => e.player))
            {
                var bsteps = events.GroupBy(cmd => cmd.TimeSpan).Where(g => g.Count() > 3);

                if (bsteps.Any())
                {
                    var bstepCount = bsteps.Max(x => x.Key);
                    yield return new Focus(
                        GetType(),
                        events.Key.HeroUnits.FirstOrDefault(u => u.Positions.Any(p => p.TimeSpan == now)),
                        events.Key,
                        settings.Weights.BStep,
                        $"{events.Key.Character} bstepping");
                }
            }

            foreach (IGrouping<Player, GameEvent> events in gameEvents.Where(e => abilityDetector.IsAbility(replay, e, settings.AbilityDetection.Taunt)).GroupBy(e => e.player))
            {
                yield return new Focus(
                    GetType(),
                    events.Key.HeroUnits.FirstOrDefault(u => u.Positions.Any(p => p.TimeSpan == now)),
                    events.Key,
                    settings.Weights.Taunt,
                    $"{events.Key.Character} taunting");
            }

            foreach (IGrouping<Player, GameEvent> events in gameEvents.Where(e => abilityDetector.IsAbility(replay, e, settings.AbilityDetection.Dance)).GroupBy(e => e.player))
            {
                yield return new Focus(
                    GetType(), 
                    events.Key.HeroUnits.FirstOrDefault(u => u.Positions.Any(p => p.TimeSpan == now)),
                    events.Key,
                    settings.Weights.Dance,
                    $"{events.Key.Character} dancing");
            }
        }
    }
}