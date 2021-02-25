using Heroes.ReplayParser;

using System.Threading.Tasks;

namespace HeroesReplay.Core.Providers
{
    public interface IReplayLoader
    {
        Task<Replay> LoadAsync(string path);
    }
}