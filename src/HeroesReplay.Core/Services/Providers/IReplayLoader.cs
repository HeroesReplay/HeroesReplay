using System.Threading.Tasks;
using Heroes.ReplayParser;

namespace HeroesReplay.Core.Services.Providers
{
    public interface IReplayLoader
    {
        Task<Replay> LoadAsync(string path);
    }
}