using System.Collections.Generic;
using HeroesReplay.Core.Replays;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// A replay whose client is not installed does not take a download slot.
/// The list cursor then moves past that page to a build that can launch.
/// </summary>
public static class ReplayDownloadPick
{
    public static ReplayListing Launchable(
        ReplayListing page,
        IEnumerable<string> installedFileVersions
    )
    {
        if (page == null)
        {
            return ReplayListing.Empty;
        }

        var kept = new List<HeroesProfileReplay>();
        if (page.Playable != null)
        {
            foreach (HeroesProfileReplay replay in page.Playable)
            {
                if (replay == null)
                {
                    continue;
                }

                if (!ReplayQueuePick.CanLaunch(replay.GameVersion, installedFileVersions))
                {
                    continue;
                }

                kept.Add(replay);
            }
        }

        return new ReplayListing(kept, page.HadRows, page.HighestId, page.NextAfter);
    }
}
