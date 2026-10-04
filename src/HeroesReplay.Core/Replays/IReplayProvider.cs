using System.Threading.Tasks;

namespace HeroesReplay.Core.Replays;

public interface IReplayProvider
{
    /// <summary>
    /// Attemps to the load the next available replay.
    /// </summary>
    /// <returns>
    /// LoadedReplay or Null
    /// </returns>
    Task<LoadedReplay> TryLoadNextReplayAsync();

    /// <summary>
    /// Put a replay already taken by <see cref="TryLoadNextReplayAsync"/> back at the front.
    /// Used when a connectivity resume has to play first.
    /// </summary>
    void Requeue(LoadedReplay replay);

    /// <summary>
    /// Leave this replay queued, but not at the front, until its deferral expires.
    /// It is not written to spectated-ids.
    /// </summary>
    void Defer(LoadedReplay replay);

    /// <summary>
    /// The match was verified complete. Record the replay as spectated for the next process,
    /// durably, before the report scenes and the next replay start.
    /// </summary>
    void MarkSpectated(LoadedReplay replay);

    /// <summary>
    /// This replay is still the current session. The next load must not return it.
    /// </summary>
    void HoldBack(int replayId);

    bool ContinuesWhenEmpty { get; }
}
