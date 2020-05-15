using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Spectator
{
    public class DebugTool
    {
        private readonly ILogger<DebugTool> logger;
        private readonly ReplayHelper replayHelper;

        public DebugTool(ILogger<DebugTool> logger, ReplayHelper replayHelper)
        {
            this.logger = logger;
            this.replayHelper = replayHelper;
        }

        public void Debug(Replay replay, TimeSpan timer)
        {
            try
            {
                PrintGatesOpen(replay, timer);
                PrintBStepping(replay, timer);
                PrintDancing(replay, timer);
                PrintTaunting(replay, timer);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not print debug data");
            }
        }

        private void PrintGatesOpen(Replay replay, TimeSpan timer)
        {
            var gatesOpening = replay.TrackerEvents
                    .Any(e => e.TrackerEventType == ReplayTrackerEvents.TrackerEventType.StatGameEvent &&
                              replayHelper.IsWithin(e.TimeSpan, timer, timer.Add(TimeSpan.FromSeconds(2))) && 
                              e.Data?.dictionary?.ContainsKey(0) == true &&
                              e.Data.dictionary[0].blobText == "GatesOpen");

            if (gatesOpening)
            {
                logger.LogDebug("Gates Opening");
            }
        }

        private void PrintDancing(Replay replay, TimeSpan timer)
        {
            IEnumerable<IGrouping<Player, GameEvent>> playerCommands = replay.GameEvents
                .Where(gameEvent => replayHelper.IsWithin(gameEvent.TimeSpan, timer, timer.Add(TimeSpan.FromSeconds(2))) && replayHelper.IsDance(replay, gameEvent))
                .GroupBy(gameEvent => gameEvent.player);

            foreach (var commands in playerCommands)
            {
                logger.LogDebug($"PrDancing: {commands.Key.HeroId}:{commands.Count()}");
            }
        }

        private void PrintTaunting(Replay replay, TimeSpan timer)
        {
            IEnumerable<IGrouping<Player, GameEvent>> playerCommands = replay.GameEvents
                .Where(gameEvent => replayHelper.IsTaunt(replay, gameEvent) && replayHelper.IsWithin(gameEvent.TimeSpan, timer, timer.Add(TimeSpan.FromSeconds(2))))
                .GroupBy(e => e.player);

            foreach (var commands in playerCommands)
            {
                logger.LogDebug($"Taunting: {commands.Key.HeroId}:{commands.Count()}");
            }
        }

        // https://github.com/ebshimizu/hots-parser/blob/97a03da10c9922ded6c64d8660756dfc95ad13a1/parser.js#L2159
        private void PrintBStepping(Replay replay, TimeSpan timer)
        {
            IEnumerable<IGrouping<Player, GameEvent>> bStepping = replay.GameEvents
                .Where(gameEvent => replayHelper.IsHearthStone(replay, gameEvent) && replayHelper.IsWithin(gameEvent.TimeSpan, timer, timer.Add(TimeSpan.FromSeconds(1))))
                .GroupBy(gameEvent => gameEvent.player);

            // This could actually be wrong, because we dont check sequence for right click, which is what cancels a hearth back, to produce the b-stepping effect
            // more than 3+ within a second is probably b-stepping behaviour
            foreach (IGrouping<Player, GameEvent> commands in bStepping.Where(cmds => cmds.Count() >= 3))
            {
                logger.LogDebug($"BStepping: {commands.Key.HeroId}:{commands.Count()}");
            }
        }
    }
}