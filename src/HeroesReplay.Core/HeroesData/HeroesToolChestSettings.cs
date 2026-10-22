using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.HeroesData;

public class HeroesToolChestSettings
{
    public Uri HeroesDataReleaseUri { get; set; }

    public IEnumerable<string> IgnoreUnits { get; set; }

    public IEnumerable<string> ObjectiveContains { get; set; }

    public IEnumerable<string> BossContains { get; set; }

    public IEnumerable<string> CampContains { get; set; }

    public IEnumerable<string> VehicleContains { get; set; }

    public IEnumerable<string> CaptureContains { get; set; }

    public string CoreScalingLinkId { get; set; }

    public IEnumerable<string> VehicleScalingLinkIds { get; set; }

    /// <summary>
    /// How often a role that loads game data asks for a newer heroes-data2 release, and downloads
    /// it when it is a newer live build than the local data (#493). Zero never asks once data is
    /// there.
    /// </summary>
    public TimeSpan RefreshInterval { get; set; }

    /// <summary>
    /// The newest heroes-data2 builds the housekeeping pass keeps in <c>Data\HeroesData</c>; older
    /// builds' files and every downloaded zip go (#493).
    /// </summary>
    public int RetainedVersions { get; set; } = 2;
}
