using System.Threading.Tasks;

namespace HeroesReplay.Service.Spectator.Core.Observer
{
    public interface ISpectator
    {
        Task SpectateAsync();
    }
}