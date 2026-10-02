namespace HeroesReplay.Core.Replays.Context;

public interface IReplayContext
{
    ContextData Previous { get; }
    ContextData Current { get; }
}
