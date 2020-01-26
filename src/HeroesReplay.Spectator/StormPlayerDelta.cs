namespace HeroesReplay.Spectator
{
    public sealed class StormPlayerDelta
    {
        public StormPlayer Previous { get; }

        public StormPlayer Current { get; }

        public StormPlayerDelta(StormPlayer previous, StormPlayer current)
        {
            Previous = previous;
            Current = current;  
        }
    }
}