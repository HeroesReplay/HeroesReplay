using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public interface IGameSession
    {
        Task SpectateAsync();
    }
}