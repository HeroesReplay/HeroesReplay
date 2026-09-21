using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Configuration;

public class SpectateSettings
{
    public IEnumerable<string> VersionsSupported { get; set; }
    public int MinDistanceToSpawn { get; set; }
    public int MaxDistanceToCore { get; set; }
    public int MaxDistanceToEnemy { get; set; }
    public int MaxDistanceToObjective { get; set; }
    public int MaxDistanceToOwnerChange { get; set; }
    public int MaxDistanceToEnemyKill { get; set; }
    public int MaxDistanceToClear { get; set; }
    public int MaxDistanceToBoss { get; set; }

    public int RetryTimerCountBeforeForceEnd { get; set; }
    public TimeSpan RetryTimerSleepDuration { get; set; }

    public TimeSpan EndScreenTime { get; set; }

    /// <summary>OCR reads per tick when the clock jumps (1–5).</summary>
    public int OcrConfirmReads { get; set; } = 3;

    /// <summary>Reject an OCR clock that leaps more than this from the last accepted time.</summary>
    public TimeSpan MaxTimerJump { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// After the core dies, consecutive OCR misses (timer hidden on end screens)
    /// required before ending the session.
    /// </summary>
    public int MissingTimerReadsToEnd { get; set; } = 3;
    public TimeSpan PanelDownTime { get; set; }
    public TimeSpan TalentsPanelStartTime { get; set; }
    public TimeSpan StatsPanelShowDuration { get; set; }
    public TimeSpan StatsPanelCooldown { get; set; }
    public TimeSpan WaitingTime { get; set; }

    public TimeSpan PastDeathContextTime { get; set; }
    public TimeSpan PresentDeathContextTime { get; set; }
    public TimeSpan KillStreakWindow { get; set; }
    public TimeSpan KillStreakHoldTime { get; set; }

    public IEnumerable<int> TalentLevels { get; set; }
}
