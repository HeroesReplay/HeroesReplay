using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Spectating.Session;

namespace HeroesReplay.Core.GameClient;

public interface IGameManager
{
    Task<ReplaySessionKind> LaunchAndSpectate(
        LoadedReplay loadedReplay,
        Func<Task<LoadedReplay>> whileReporting
    );

    /// <summary>
    /// Close Heroes after repeated attempts that never became a match, so the next
    /// replay can launch. Battle.net is left alone.
    /// </summary>
    void ReleaseClientAfterDefer();

    MatchOutcome LastOutcome { get; }

    /// <summary>The last session read the match clock. False after a hold.</summary>
    bool LastMatchClockSeen { get; }
}
