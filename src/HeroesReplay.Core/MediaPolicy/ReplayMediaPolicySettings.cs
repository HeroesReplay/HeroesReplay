using System;
using System.Collections.Generic;
using HeroesReplay.Core.YouTube.Publication;

namespace HeroesReplay.Core.MediaPolicy;

/// <summary>
/// History-independent recording and publication rules.
/// Defaults record nothing and publish nothing. Invalid values fail closed.
/// Algorithm defaults match the canary publication schedule until a key is set.
/// </summary>
public class ReplayMediaPolicySettings
{
    public string Version { get; set; } = "1";

    public ReplayRecordingMode RecordingMode { get; set; } = ReplayRecordingMode.Disabled;

    public ReplayPublicationMode PublicationMode { get; set; } = ReplayPublicationMode.Disabled;

    /// <summary>Binder failures. Empty when the section was valid or absent.</summary>
    public IReadOnlyList<string> LoadErrors { get; set; } = Array.Empty<string>();

    public int MaxPublicPerDay { get; set; } = PublicationSchedule.MaxPublicPerDay;

    public int MaxPublicPerWeek { get; set; } = PublicationSchedule.MaxPublicPerWeek;

    public TimeSpan MinimumPublicInterval { get; set; } = PublicationSchedule.MinimumInterval;

    public TimeSpan MapCooldown { get; set; } = PublicationSchedule.DiversityCooldown;

    public TimeSpan RankCooldown { get; set; } = PublicationSchedule.DiversityCooldown;

    public TimeSpan FeaturedHeroCooldown { get; set; } = PublicationSchedule.DiversityCooldown;

    /// <summary>Roster overlap that defers an ordinary upload. Zero disables the roster check.</summary>
    public int MaxSharedHeroes { get; set; } = PublicationSchedule.MaxSharedHeroes;

    public int ReservedRequestSlotsPerDay { get; set; } =
        PublicationSchedule.ReservedRequestSlotsPerDay;

    public int MaxInsertsPerQuotaDay { get; set; } = PublicationSchedule.MaxInsertsPerQuotaDay;

    /// <summary>When true, <see cref="MinimumGameVersion"/> is required and off-patch replays are rejected.</summary>
    public bool RequireCurrentPatch { get; set; }

    public string MinimumGameVersion { get; set; }

    /// <summary>Paid <c>RecordAndUpload</c> requests may ignore <see cref="MinimumGameVersion"/>.</summary>
    public bool RequestsBypassPatchRequirement { get; set; } = true;

    /// <summary>Blank means rank is not a high-skill signal. Compared by ladder, division ignored.</summary>
    public string MinimumHighSkillRank { get; set; }

    /// <summary>Null means MMR is not a high-skill signal. A replay qualifies when MMR is at least this value.</summary>
    public int? MinimumHighSkillMmr { get; set; }

    public TimeSpan OrdinaryCandidateMaxAge { get; set; } = PublicationSchedule.OrdinaryMaxAge;

    public TimeSpan HighSkillCandidateMaxAge { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan NotableCandidateMaxAge { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan RequestedCandidateMaxAge { get; set; } = TimeSpan.FromDays(14);
}
