namespace HeroesReplay
{
    public sealed class StateDelta
    {
        public State Previous { get; }
        public State Current { get; }

        public StateDelta(State previous, State current)
        {
            Previous = previous;
            Current = current;
        }
    }
}