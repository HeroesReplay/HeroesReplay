using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Spectating;

public class SpectateSettings
{
    public IEnumerable<string> VersionsSupported { get; set; }

    /// <summary>Oldest client that may be spectated, inclusive. Newer builds are allowed.</summary>
    public string MinimumGameVersion { get; set; }
    public int MinDistanceToSpawn { get; set; }
    public int MaxDistanceToCore { get; set; }

    public TimeSpan EndingCoreWindow { get; set; }
    public int MaxDistanceToEnemy { get; set; }
    public int MaxDistanceToObjective { get; set; }
    public int MaxDistanceToOwnerChange { get; set; }
    public int MaxDistanceToEnemyKill { get; set; }
    public int MaxDistanceToClear { get; set; }
    public int MaxDistanceToBoss { get; set; }

    public int RetryTimerCountBeforeForceEnd { get; set; }
    public TimeSpan RetryTimerSleepDuration { get; set; }

    public TimeSpan EndScreenTime { get; set; }

    public TimeSpan PanelDownTime { get; set; }
    public TimeSpan TalentsPanelStartTime { get; set; }
    public TimeSpan TalentPanelHold { get; set; } = TimeSpan.FromSeconds(8);
    public TimeSpan TalentPanelCluster { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan StatsPanelShowDuration { get; set; }
    public TimeSpan StatsPanelCooldown { get; set; }
    public TimeSpan WaitingTime { get; set; }

    public TimeSpan PastDeathContextTime { get; set; }
    public TimeSpan PresentDeathContextTime { get; set; }
    public TimeSpan KillStreakWindow { get; set; }
    public TimeSpan KillStreakHoldTime { get; set; }

    public IEnumerable<int> TalentLevels { get; set; }
}
