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

    /// <summary>
    /// Heroes whose body stays out of the action (Abathur plays through his Symbiote). Their body
    /// gets no roaming or proximity focus unless an enemy hero is within MaxDistanceToEnemy.
    /// </summary>
    public IEnumerable<string> RemoteBodyHeroes { get; set; } = Array.Empty<string>();

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

    /// <summary>
    /// How long a launch waits on a matching client that shows no menu, loading screen, match,
    /// match clock, game data, or blank startup window before it recovers that client once:
    /// the current patch is closed and signed in again, a previous patch gets the replay through
    /// HeroesSwitcher again (#249). Zero or less means the default, 3 minutes.
    /// </summary>
    public TimeSpan LaunchWaitLimit { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// How long a replay on an older build that is not installed waits, after it was handed to
    /// HeroesSwitcher, for Blizzard to put that build's exe in <c>Versions\Base*</c>. The launch
    /// deadline is extended while it waits. When the exe has not appeared by then, the client is
    /// closed, the replay is deferred as <c>BuildNotInstalled</c>, and the build is held for
    /// <see cref="BuildDownloadHold"/>. Zero or less means the default, 10 minutes.
    /// </summary>
    public TimeSpan BuildDownloadLimit { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a build whose download failed counts as not installed
    /// (<c>Data\client-download-holds.json</c>), for the launch, the spectate queue, and the
    /// download role. Zero or less means the default, 4 hours.
    /// </summary>
    public TimeSpan BuildDownloadHold { get; set; } = TimeSpan.FromHours(4);
}
