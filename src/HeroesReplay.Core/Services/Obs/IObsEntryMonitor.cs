using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware
{
    public interface IObsEntryMonitor
    {
        Task ListenAsync();
    }
}
