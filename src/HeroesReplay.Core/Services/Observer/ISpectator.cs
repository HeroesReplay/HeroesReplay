using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Observer;

public interface ISpectator
{
    bool MatchClockSeen { get; }

    MatchOutcome Outcome { get; }

    void RecordHold(ClientHoldReason hold);

    Task SpectateAsync();
}
