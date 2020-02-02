using System;
using System.Threading.Tasks;
using HeroesReplay.Processes;
using HeroesReplay.Shared;

namespace HeroesReplay.Spectator
{
    public class GameStateTool
    {
        private readonly HeroesOfTheStorm heroesOfTheStorm;

        public GameStateTool(HeroesOfTheStorm heroesOfTheStorm)
        {
            this.heroesOfTheStorm = heroesOfTheStorm;
        }

        public async Task<(TimeSpan Next, GameState State)> GetStateAsync(StormReplay stormReplay, TimeSpan timer, GameState state)
        {
            TimeSpan? elapsed = await heroesOfTheStorm.TryGetTimerAsync();

            if (elapsed != null && elapsed != TimeSpan.Zero)
            {
                TimeSpan next = elapsed.Value.RemoveNegativeOffset();

                if (next <= TimeSpan.Zero) return (next, GameState.StartOfGame);
                if (next > timer) return (next, GameState.Running);
                if (next <= timer) return (next, GameState.Paused);
                return (next, state);
            }

            if (timer.IsNearEnd(stormReplay.Replay.ReplayLength) && await heroesOfTheStorm.TryGetMatchAwardsAsync(stormReplay.Replay.GetMatchAwards()))
            {
                return (timer, GameState.EndOfGame);
            }

            return (timer, state);
        }
    }
}