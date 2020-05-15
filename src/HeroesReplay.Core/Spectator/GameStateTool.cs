using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Processes;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Spectator
{
    public class GameStateTool
    {
        private readonly ILogger<GameStateTool> logger;
        private readonly HeroesOfTheStorm heroesOfTheStorm;
        private readonly ReplayHelper replayHelper;

        public GameStateTool(ILogger<GameStateTool> logger, HeroesOfTheStorm heroesOfTheStorm, ReplayHelper replayHelper)
        {
            this.logger = logger;
            this.heroesOfTheStorm = heroesOfTheStorm;
            this.replayHelper = replayHelper;
        }

        public async Task<StormState> GetStateAsync(Replay replay, StormState currentState)
        {
            TimeSpan? elapsed = await heroesOfTheStorm.TryGetTimerAsync();

            if (elapsed != null && elapsed != TimeSpan.Zero)
            {
                TimeSpan next = replayHelper.RemoveNegativeOffset(elapsed.Value);

                if (next <= TimeSpan.Zero) return new StormState(next, GameState.StartOfGame);
                if (next > currentState.Timer) return new StormState(next, GameState.Running);
                if (next < currentState.Timer) return new StormState(next, GameState.Paused);
                return new StormState(next, currentState.State);
            }

            // Did the core die at this time?
            if (replay.Units.Any(unit => replayHelper.IsCore(unit) && unit.TimeSpanDied.GetValueOrDefault(TimeSpan.MaxValue) <= currentState.Timer))
            {
                logger.LogInformation("END OF GAME. Core unit found dead at: " + currentState.Timer);
                return new StormState(currentState.Timer, GameState.EndOfGame);
            }

            if (replayHelper.IsNearEnd(currentState, replay.ReplayLength))
            {
                if (await heroesOfTheStorm.TryGetMatchAwardsAsync(replay.Players.SelectMany(p => p.ScoreResult.MatchAwards).Distinct()))
                {
                    logger.LogInformation("END OF GAME. Match award found at: " + currentState.Timer);
                    return new StormState(currentState.Timer, GameState.EndOfGame);
                }
            }

            return currentState;
        }
    }
}