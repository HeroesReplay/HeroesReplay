using System.Collections.Generic;

namespace HeroesReplay.Core.Services.HeroesProfile;

/// <summary>
/// The on-disk cache uses the same floor as the downloader. An older file stays
/// on disk and is not treated as spectated.
/// </summary>
public static class ReplayFloor
{
    public static bool Allows(
        string version,
        IEnumerable<string> exactVersions,
        string minimumVersion
    )
    {
        return GameVersionOrder.Allows(version, exactVersions, minimumVersion);
    }
}
