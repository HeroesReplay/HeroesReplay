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

/// <summary>
/// Paid uploads are <see cref="Requested"/>. The rest rank by their score weight: Requested,
/// then <see cref="Focus"/>, Notable, HighSkill and Ordinary. The value is never compared; it is
/// stored by name, so <see cref="Focus"/> is added last and an older build reading it refuses
/// the manifest instead of taking it for another priority.
/// </summary>
public enum ReplayMediaPriority
{
    Ordinary = 0,
    HighSkill = 1,
    Notable = 2,
    Requested = 3,

    /// <summary>A focus-hero replay the cycle picked itself (#433).</summary>
    Focus = 4,
}
