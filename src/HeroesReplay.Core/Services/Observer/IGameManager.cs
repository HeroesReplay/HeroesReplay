using System.Threading.Tasks;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Observer
{
    public interface IGameManager
    {
        Task LaunchAndSpectate(LoadedReplay loadedReplay);
    }
}