using System.Threading.Tasks;
using Heroes.ReplayParser;

namespace HeroesReplay.Core.Replays;

public interface IReplayLoader
{
    Task<Replay> LoadAsync(string path);
}
