using System.Collections.Generic;
using HeroesReplay.Core.GameClient;

namespace HeroesReplay.Core.Replays;

/// <summary>
/// Queue selection uses the same installed-client check as launch. An older build that is not
/// installed can launch: HeroesSwitcher opens it and Blizzard downloads that client. A build
/// newer than the current patch, or one held after a failed download
/// (<see cref="ClientDownloadHold"/>), stays queued and is not the replay that parks the
/// waiting scene.
/// </summary>
public static class ReplayQueuePick
{
    public static bool CanLaunch(
        string replayVersion,
        IEnumerable<string> installedFileVersions,
        IEnumerable<string> heldBuilds = null
    )
    {
        return ReplayClientRoute.Classify(replayVersion, installedFileVersions, heldBuilds)
            != ReplayClientPatch.NotInstalled;
    }
}
