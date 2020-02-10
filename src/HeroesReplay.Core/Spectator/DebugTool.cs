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

        public DebugTool(ILogger<DebugTool> logger)
        {
            this.logger = logger;
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
            foreach (TrackerEvent gatesOpen in replay.TrackerEvents.Where(e => e.TrackerEventType == ReplayTrackerEvents.TrackerEventType.StatGameEvent && 
                                                                               e.TimeSpan.IsWithin(timer, timer.Add(TimeSpan.FromSeconds(2))) && e.Data?.dictionary?.ContainsKey(0) == true && 
                                                                               e.Data.dictionary[0].blobText == "GatesOpen"))
            {
                logger.LogDebug("Gates Opening");
            }
        }

        private void PrintDancing(Replay replay, TimeSpan timer)
        {
            IEnumerable<IGrouping<Player, GameEvent>> playerCommands = replay.GameEvents
                .Where(e => e.TimeSpan.IsWithin(timer, timer.Add(TimeSpan.FromSeconds(2))) && replay.IsDance(e))
                .GroupBy(ge => ge.player);

            foreach (var commands in playerCommands)
            {
                logger.LogInformation("{0} dancing: {1}", commands.Key.HeroId, commands.Count());
            }
        }

        private void PrintTaunting(Replay replay, TimeSpan timer)
        {
            IEnumerable<IGrouping<Player, GameEvent>> playerCommands = replay.GameEvents
                .Where(e => replay.IsTaunt(e) && e.TimeSpan.IsWithin(timer, timer.Add(TimeSpan.FromSeconds(2))))
                .GroupBy(e => e.player);

            foreach (var commands in playerCommands)
            {
                logger.LogInformation("{0} taunting: {1}", commands.Key.HeroId, commands.Count());
            }
        }

        // https://github.com/ebshimizu/hots-parser/blob/97a03da10c9922ded6c64d8660756dfc95ad13a1/parser.js#L2159
        private void PrintBStepping(Replay replay, TimeSpan timer)
        {
            IEnumerable<IGrouping<Player, GameEvent>> bStepping = replay.GameEvents
                .Where(e => replay.IsHearthStone(e) && e.TimeSpan.IsWithin(timer, timer.Add(TimeSpan.FromSeconds(1))))
                .GroupBy(e => e.player);

            // This could actually be wrong, because we dont check sequence for right click, which is what cancels a hearth back, to produce the b-stepping effect
            foreach (IGrouping<Player, GameEvent> commands in bStepping.Where(cmds => cmds.Count() > 1))
            {
                logger.LogInformation("{0} bstepping: {1}", commands.Key.HeroId, commands.Count());
            }
        }
    }
}