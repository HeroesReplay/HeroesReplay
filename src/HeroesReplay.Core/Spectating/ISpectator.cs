using System.Threading.Tasks;
using HeroesReplay.Core.GameClient;

namespace HeroesReplay.Core.Spectating;

public interface ISpectator
{
    bool MatchClockSeen { get; }

    MatchOutcome Outcome { get; }

    void RecordHold(ClientHoldReason hold);

    Task SpectateAsync();
}
