using System;

namespace HeroesReplay.Core.Spectator
{
    public class StormState : IEquatable<StormState>
    {
        public TimeSpan Timer { get; }
        public GameState State { get; }

        public static StormState Start { get; } = new StormState(TimeSpan.Zero, GameState.StartOfGame);

        public StormState(TimeSpan timer, GameState state)
        {
            Timer = timer;
            State = state;
        }

        public override string ToString() => $"timer: {Timer}, state: {State}";

        public bool Equals(StormState other)
        {
            return State.Equals(other.State);
        }
    }
}