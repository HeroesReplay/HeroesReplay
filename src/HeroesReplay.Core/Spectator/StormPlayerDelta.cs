namespace HeroesReplay.Core.Spectator
{
    public sealed class Delta<T>
    {
        public T Previous { get; }

        public T Current { get; }

        public Delta(T previous, T current)
        {
            Previous = previous;
            Current = current;  
        }
    }
}