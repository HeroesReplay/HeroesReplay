using HeroesReplay.Core.Models;

using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public interface IGameManager
    {
        Task SpectateAsync(StormReplay stormReplay);
    }
}