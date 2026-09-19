using System.Collections.Generic;

namespace HeroesReplay.Core.Configuration;

public class TrackerEventSettings
{
    public string GatesOpen { get; set; }
    public string TalentChosen { get; set; }
    public string JungleCampCapture { get; set; }

    /// <summary>
    /// StatGameEvent names that mark the end screen (MVP votes, XP breakdown, …).
    /// Session end uses the latest matching time. From Heroes.ReplayParser StatGameEvent.
    /// </summary>
    public IEnumerable<string> EndOfGameStatEvents { get; set; }

    /// <summary>
    /// When true, also consider TrackerEventType.ScoreResultEvent (score table, event id 11).
    /// </summary>
    public bool UseScoreResultEvent { get; set; }
}
