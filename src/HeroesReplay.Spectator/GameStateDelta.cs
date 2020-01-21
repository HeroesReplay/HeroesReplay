namespace HeroesReplay.Spectator
{
    public sealed class GameStateDelta
    {
        public GameState Previous { get; }
        public GameState Current { get; }

        public GameStateDelta(GameState previous, GameState current)
        {
            Previous = previous;
            Current = current;
        }
    }
}