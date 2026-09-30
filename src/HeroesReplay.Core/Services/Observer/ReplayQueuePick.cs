using System.Collections.Generic;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// Queue selection uses the same installed-client check as launch. A missing exe stays
/// queued and is not the replay that parks the waiting scene.
/// </summary>
public static class ReplayQueuePick
{
    public static bool CanLaunch(string replayVersion, IEnumerable<string> installedFileVersions)
    {
        return ReplayClientRoute.Classify(replayVersion, installedFileVersions)
            != ReplayClientPatch.NotInstalled;
    }
}
