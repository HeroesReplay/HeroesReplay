using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Observer;

public interface ISpectator
{
    bool MatchClockSeen { get; }

    Task SpectateAsync();
}
