namespace HeroesReplay
{
    public class StateDelta
    {
        public GameState Previous { get; }
        public GameState Current { get; }

        public StateDelta(GameState previous, GameState current)
        {
            this.Previous = previous;
            this.Current = current;
        }
    }
}