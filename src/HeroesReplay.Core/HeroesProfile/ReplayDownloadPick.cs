using System.Collections.Generic;
using HeroesReplay.Core.GameClient;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// A replay takes a download slot when spectate can launch it: an installed build, or an older
/// build on the supported patch line (<c>Spectate:MinimumGameVersion</c> and newer) that is not
/// installed, because HeroesSwitcher opens it and Blizzard downloads that client. A build newer
/// than the current patch, or one held after a failed download (<see cref="ClientDownloadHold"/>),
/// does not take a slot. The list cursor then moves past that page to a build that can launch.
/// </summary>
public static class ReplayDownloadPick
{
    public static ReplayListing Launchable(
        ReplayListing page,
        IEnumerable<string> installedFileVersions,
        IEnumerable<string> heldBuilds = null,
        string minimumVersion = null
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

                ReplayClientPatch patch = ReplayClientRoute.Classify(
                    replay.GameVersion,
                    installedFileVersions,
                    heldBuilds
                );
                if (patch == ReplayClientPatch.NotInstalled)
                {
                    continue;
                }

                if (
                    patch == ReplayClientPatch.Download
                    && !ClientBuildArchive.ShouldKeep(replay.GameVersion, minimumVersion)
                )
                {
                    continue;
                }

                kept.Add(replay);
            }
        }

        return new ReplayListing(kept, page.HadRows, page.HighestId, page.NextAfter);
    }

    /// <summary>
    /// The lowest id on the newest installed build, the current patch. Upload order does not
    /// follow the client build: a newer id can still be an older client. With no client read,
    /// every replay counts as current, so this is the lowest id.
    /// </summary>
    public static HeroesProfileReplay FirstOnCurrentPatch(
        IEnumerable<HeroesProfileReplay> candidates,
        IEnumerable<string> installedFileVersions
    )
    {
        if (candidates == null)
        {
            return null;
        }

        HeroesProfileReplay first = null;
        foreach (HeroesProfileReplay replay in candidates)
        {
            if (
                replay == null
                || ReplayClientRoute.Classify(replay.GameVersion, installedFileVersions)
                    != ReplayClientPatch.Current
            )
            {
                continue;
            }

            if (first == null || replay.Id < first.Id)
            {
                first = replay;
            }
        }

        return first;
    }
}
