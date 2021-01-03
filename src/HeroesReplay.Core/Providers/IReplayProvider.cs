using HeroesReplay.Core.Models;

using System.Threading.Tasks;

namespace HeroesReplay.Core.Providers
{
    public interface IReplayProvider
    {
        Task<StormReplay> TryLoadReplayAsync();
    }
}