using System.Threading.Tasks;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Spectating.Session;

namespace HeroesReplay.Core.Spectating;

public interface ISpectator
{
    bool MatchClockSeen { get; }

    MatchOutcome Outcome { get; }

    void RecordHold(ClientHoldReason hold);

    Task SpectateAsync();
}
