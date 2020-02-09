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

        public void Debug(StormReplay stormReplay, TimeSpan timer)
        {
            try
            {
                PrintGatesOpen(stormReplay, timer);
                PrintBStepping(stormReplay, timer);
                PrintDancing(stormReplay, timer);
                PrintTaunting(stormReplay, timer);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not print debug data");
            }
        }

        private void PrintGatesOpen(StormReplay stormReplay, in TimeSpan timer)
        {
            foreach (TrackerEvent gatesOpen in stormReplay.Replay.TrackerEvents.Where(te => te.TrackerEventType == ReplayTrackerEvents.TrackerEventType.StatGameEvent && te.Data.blobText == "GatesOpen"))
            {
                logger.LogDebug(gatesOpen.ToString());
            }
        }

        private void PrintDancing(StormReplay stormReplay, TimeSpan timer)
        {
            IEnumerable<IGrouping<Player, GameEvent>> playerCommands =

                stormReplay.Replay.GameEvents.Where(gameEvent => gameEvent.TimeSpan.IsWithin(timer, timer.Add(TimeSpan.FromSeconds(2))) && stormReplay.Replay.IsDance(gameEvent))
                .GroupBy(ge => ge.player);

            foreach (var commands in playerCommands)
            {
                logger.LogInformation("{0} dancing: {1}", commands.Key.HeroId, commands.Count());
            }
        }

        private void PrintTaunting(StormReplay stormReplay, TimeSpan timer)
        {
            IEnumerable<IGrouping<Player, GameEvent>> playerCommands = stormReplay.Replay.GameEvents
                    .Where(gameEvent => stormReplay.Replay.IsTaunt(gameEvent) && gameEvent.TimeSpan.IsWithin(timer, timer.Add(TimeSpan.FromSeconds(2))))
                    .GroupBy(ge => ge.player);

            foreach (var commands in playerCommands)
            {
                logger.LogInformation("{0} taunting: {1}", commands.Key.HeroId, commands.Count());
            }
        }

        // https://github.com/ebshimizu/hots-parser/blob/97a03da10c9922ded6c64d8660756dfc95ad13a1/parser.js#L2159
        private void PrintBStepping(StormReplay stormReplay, TimeSpan timer)
        {
            IEnumerable<IGrouping<Player, GameEvent>> bStepping = stormReplay.Replay.GameEvents
                .Where(gameEvent => stormReplay.Replay.IsHearthStone(gameEvent) && gameEvent.TimeSpan.IsWithin(timer, timer.Add(TimeSpan.FromSeconds(1))))
                .GroupBy(ge => ge.player);

            // This could actually be wrong, because we dont check sequence for right click, which is what cancels a hearth back, to produce the b-stepping effect
            foreach (IGrouping<Player, GameEvent> commands in bStepping.Where(cmds => cmds.Count() > 1))
            {
                logger.LogInformation("{0} bstepping: {1}", commands.Key.HeroId, commands.Count());
            }
        }
    }
}