using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.HeroesProfile;

namespace HeroesReplay.Core.Requests;

public static class RewardCandidateFilter
{
    public static HeroesProfileReplay Choose(
        IEnumerable<HeroesProfileReplay> replays,
        ISet<int> playedIds,
        ISet<int> queuedIds,
        IEnumerable<string> versionsSupported,
        string minimumVersion = null
    )
    {
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (versionsSupported != null)
        {
            foreach (string version in versionsSupported)
            {
                if (!string.IsNullOrWhiteSpace(version))
                {
                    versions.Add(version.Trim());
                }
            }
        }

        List<HeroesProfileReplay> pool = (replays ?? Enumerable.Empty<HeroesProfileReplay>())
            .Where(replay => replay != null && replay.Id > 0)
            .Where(replay => replay.Deleted is not > 0)
            .Where(replay => replay.Downloadable != false)
            .Where(replay => GameVersionOrder.Allows(replay.GameVersion, versions, minimumVersion))
            .Where(replay => playedIds == null || !playedIds.Contains(replay.Id))
            .Where(replay => queuedIds == null || !queuedIds.Contains(replay.Id))
            .ToList();
        if (pool.Count == 0)
        {
            return null;
        }

        return pool[Random.Shared.Next(pool.Count)];
    }
}
