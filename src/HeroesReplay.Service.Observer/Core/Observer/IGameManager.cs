using System.Threading.Tasks;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Service.Spectator.Core.Observer
{
    public interface IGameManager
    {
        Task LaunchAndSpectate(LoadedReplay loadedReplay);
    }
}