using System;

namespace HeroesReplay.Core.Services.Media;

/// <summary>
/// History-independent recording and publication rules.
/// Defaults record nothing and publish nothing. Invalid values fail closed.
/// </summary>
public class ReplayMediaPolicySettings
{
    public string Version { get; set; } = "1";

    public ReplayRecordingMode RecordingMode { get; set; } = ReplayRecordingMode.Disabled;

    public ReplayPublicationMode PublicationMode { get; set; } = ReplayPublicationMode.Disabled;

    /// <summary>When true, <see cref="MinimumGameVersion"/> is required and off-patch replays are rejected.</summary>
    public bool RequireCurrentPatch { get; set; }

    public string MinimumGameVersion { get; set; }

    /// <summary>Paid <c>RecordAndUpload</c> requests may ignore <see cref="MinimumGameVersion"/>.</summary>
    public bool RequestsBypassPatchRequirement { get; set; } = true;

    /// <summary>Blank means rank is not a high-skill signal. Compared by ladder, division ignored.</summary>
    public string MinimumHighSkillRank { get; set; }

    /// <summary>Null means MMR is not a high-skill signal. A replay qualifies when MMR is at least this value.</summary>
    public int? MinimumHighSkillMmr { get; set; }

    public TimeSpan OrdinaryCandidateMaxAge { get; set; } = TimeSpan.FromDays(3);

    public TimeSpan HighSkillCandidateMaxAge { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan NotableCandidateMaxAge { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan RequestedCandidateMaxAge { get; set; } = TimeSpan.FromDays(14);
}
