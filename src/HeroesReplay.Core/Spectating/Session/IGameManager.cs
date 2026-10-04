using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Replays;

namespace HeroesReplay.Core.Spectating.Session;

public interface IGameManager
{
    /// <summary>
    /// Plays one replay through to the next replay's handoff. <paramref name="outcomeKnown"/>
    /// hears the session's outcome once, as soon as it is final: after the recording stops and
    /// Heroes closes, before <paramref name="whileReporting"/> and the report scenes.
    /// </summary>
    Task<ReplaySessionKind> LaunchAndSpectate(
        LoadedReplay loadedReplay,
        Action<ReplaySessionKind> outcomeKnown,
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
