using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class PlayerTauntsWeightings : IGameWeightings
    {
        // https://github.com/ebshimizu/hots-parser/blob/master/parser.js#L2185
        public IEnumerable<(Unit Unit, Player Player, double Points, string Description)> GetPlayers(TimeSpan now, Replay replay)
        {
            IEnumerable<GameEvent> gameEvents = replay.GameEvents.Where(e => e.TimeSpan == now);

            foreach (IGrouping<Player, GameEvent> events in gameEvents.Where(e => IsHearthStone(replay, e)).GroupBy(e => e.player))
            {
                var bsteps = events.GroupBy(cmd => cmd.TimeSpan).Where(g => g.Count() > 3);

                if (bsteps.Any())
                {
                    var bstepCount = bsteps.Max(x => x.Key);
                    yield return (events.Key.HeroUnits.FirstOrDefault(u => u.Positions.Any(p => p.TimeSpan == now)), events.Key, Constants.Weights.TauntingBStep, $"{events.Key.HeroId} bstepping");
                }
            }

            foreach (IGrouping<Player, GameEvent> events in gameEvents.Where(e => IsTaunt(replay, e)).GroupBy(e => e.player))
            {
                yield return (events.Key.HeroUnits.FirstOrDefault(u => u.Positions.Any(p => p.TimeSpan == now)), events.Key, Constants.Weights.TauntingEmote, $"{events.Key.HeroId} taunting");
            }

            foreach (IGrouping<Player, GameEvent> events in gameEvents.Where(e => IsDance(replay, e)).GroupBy(e => e.player))
            {
                yield return (events.Key.HeroUnits.FirstOrDefault(u => u.Positions.Any(p => p.TimeSpan == now)), events.Key, Constants.Weights.TauntingEmote, $"{events.Key.HeroId} dancing");
            }
        }

        private bool IsTaunt(Replay replay, GameEvent gameEvent)
        {
            return gameEvent.eventType == GameEventType.CCmdEvent && GetAbilityCmdIndex(gameEvent.data) == 4 &&
                (
                    GetAbilityLink(gameEvent.data) == 19 && replay.ReplayBuild < 68740 ||
                    GetAbilityLink(gameEvent.data) == 22 && replay.ReplayBuild >= 68740
                );
        }

        private bool IsDance(Replay replay, GameEvent gameEvent)
        {
            return gameEvent.eventType == GameEventType.CCmdEvent && GetAbilityCmdIndex(gameEvent.data) == 3 &&
                (
                    GetAbilityLink(gameEvent.data) == 19 && replay.ReplayBuild < 68740 ||
                    GetAbilityLink(gameEvent.data) == 22 && replay.ReplayBuild >= 68740
                );
        }

        private bool IsHearthStone(Replay replay, GameEvent gameEvent)
        {
            return gameEvent.eventType == GameEventType.CCmdEvent &&
                (
                    (replay.ReplayBuild < 61872 && GetAbilityLink(gameEvent.data) == 200) ||
                    (replay.ReplayBuild >= 61872 && replay.ReplayBuild < 68740 && GetAbilityLink(gameEvent.data) == 119) ||
                    (replay.ReplayBuild >= 68740 && replay.ReplayBuild < 70682 && GetAbilityLink(gameEvent.data) == 116) ||
                    (replay.ReplayBuild >= 70682 && replay.ReplayBuild < 77525 && GetAbilityLink(gameEvent.data) == 112) ||
                    (replay.ReplayBuild >= 77525 && replay.ReplayBuild < 79033 && GetAbilityLink(gameEvent.data) == 114) ||
                    (replay.ReplayBuild >= 79033 && GetAbilityLink(gameEvent.data) == 115)
                );
        }

        private int GetAbilityLink(TrackerEventStructure structure)
        {
            return Convert.ToInt32(structure?.array[1]?.array[0]?.unsignedInt.GetValueOrDefault()); // m_abilLink
        }

        private int GetAbilityCmdIndex(TrackerEventStructure trackerEvent)
        {
            return Convert.ToInt32(trackerEvent.array[1]?.array[1]?.unsignedInt.GetValueOrDefault()); // m_abilCmdIndex
        }
    }
}