using System.Threading.Tasks;

namespace HeroesReplay.Service.Obs.Core
{
    public interface IObsEntryMonitor
    {
        Task ListenAsync();
    }
}
