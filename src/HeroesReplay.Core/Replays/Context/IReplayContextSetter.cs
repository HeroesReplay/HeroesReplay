using System.Threading.Tasks;

namespace HeroesReplay.Core.Replays.Context;

public interface IReplayContextSetter
{
    Task SetContextAsync(LoadedReplay stormReplay);
}
