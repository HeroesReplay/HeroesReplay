using System.Collections.Generic;

namespace HeroesReplay.Core.Configuration;

public class TrackerEventSettings
{
    public string GatesOpen { get; set; }
    public string TalentChosen { get; set; }
    public string JungleCampCapture { get; set; }

    /// <summary>
    /// StatGameEvent names that mark the end screen (e.g. EndOfGameUpVotesCollected).
    /// These may extend the watch past CoreKilled + EndScreenTime; they do not
    /// replace the core-death hold. ScoreResultEvent is only used when none of
    /// these names appear in the replay.
    /// </summary>
    public IEnumerable<string> EndOfGameStatEvents { get; set; }

    /// <summary>
    /// Fallback when no EndOfGameStatEvents are present: TrackerEventType.ScoreResultEvent (id 11).
    /// </summary>
    public bool UseScoreResultEvent { get; set; }
}
