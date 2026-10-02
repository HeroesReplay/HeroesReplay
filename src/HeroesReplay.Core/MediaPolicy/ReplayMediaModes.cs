namespace HeroesReplay.Core.MediaPolicy;

/// <summary>Who gets an OBS recording. The default records nobody.</summary>
public enum ReplayRecordingMode
{
    Disabled = 0,
    RequestedOnly = 1,
    Selected = 2,
    All = 3,
}

/// <summary>
/// Who may become a YouTube publication candidate.
/// <see cref="AllEligible"/> is the compatibility path and is not the default.
/// </summary>
public enum ReplayPublicationMode
{
    Disabled = 0,
    RequestedOnly = 1,
    Curated = 2,
    AllEligible = 3,
}

/// <summary>Higher values outrank lower ones. Paid uploads are <see cref="Requested"/>.</summary>
public enum ReplayMediaPriority
{
    Ordinary = 0,
    HighSkill = 1,
    Notable = 2,
    Requested = 3,
}
