using HeroesReplay.Core.Obs.Collection;

namespace HeroesReplay.Core.Obs.Inspection;

/// <summary>
/// What the read-only OBS agent tools read from configuration on each call: the effective
/// <c>OBS</c> section, the install that owns the packaged <c>obs</c> folder, and
/// <c>Location:DataDirectory</c> for the same path rewriting <see cref="ObsCollectionPatcher"/>
/// applies. <see cref="Armed"/> is this machine's <see cref="ObsStreamArm"/>.
/// </summary>
public sealed record ObsInspectionSettings(
    OBSSettings Obs,
    string InstallDirectory,
    string DataDirectory,
    bool Armed,
    string ArmFile
);
