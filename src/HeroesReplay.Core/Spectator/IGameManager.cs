using HeroesReplay.Core.Shared;

using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public interface IGameManager
    {
        Task SetSessionAsync(StormReplay stormReplay);
        Task SpectateSessionAsync();
    }
}