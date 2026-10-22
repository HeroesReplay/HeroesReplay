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

    /// <summary>
    /// How far ahead a public video may be scheduled. A replay whose earliest allowed publish
    /// time is later than this stays on disk and is tried again on a later pass. Seven days
    /// keeps a scheduled video among the newest 50 uploads, the page the library pass always
    /// reads, when it goes public.
    /// </summary>
    public TimeSpan MaxPublishAhead { get; set; } = PublicationSchedule.PublishAhead;

    public TimeSpan MapCooldown { get; set; } = PublicationSchedule.DiversityCooldown;

    public TimeSpan RankCooldown { get; set; } = PublicationSchedule.DiversityCooldown;

    public TimeSpan FeaturedHeroCooldown { get; set; } = PublicationSchedule.DiversityCooldown;

    /// <summary>Roster overlap that defers an ordinary upload. Zero disables the roster check.</summary>
    public int MaxSharedHeroes { get; set; } = PublicationSchedule.MaxSharedHeroes;

    public int ReservedRequestSlotsPerDay { get; set; } =
        PublicationSchedule.ReservedRequestSlotsPerDay;

    public int MaxInsertsPerQuotaDay { get; set; } = PublicationSchedule.MaxInsertsPerQuotaDay;

    /// <summary>
    /// A replay that is not a viewer request is recorded only while its recording would still be
    /// uploaded before the replay expires: the publication window (<see cref="MaxPublishAhead"/>)
    /// has a free slot, or the recordings ahead of it drain at the pacing rules' pace with a day
    /// to spare. The waiting recordings must also stay under <see cref="MaxWaitingRecordings"/>
    /// (<see cref="RecordingCap"/>, #250, #370, #507). False records whatever the mode selects.
    /// </summary>
    public bool CapRecordingToPublication { get; set; } = true;

    /// <summary>
    /// Recordings that may wait on disk for their <c>videos.insert</c> before the recording cap
    /// stops recording a replay that is not a viewer request (#507). Zero or less means one day of
    /// upload calls. A larger value is held to one day of upload calls too. Production keeps it to
    /// a few hours of games, so a stalled uploader stops new recordings within hours instead of
    /// letting a day of uploads (about 220 GB at 95 a day) pile up.
    /// </summary>
    public int MaxWaitingRecordings { get; set; }

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

    /// <summary>
    /// A focus-hero replay's window (#433). Its game may already be days old when it is picked:
    /// the focus picker reaches back past the ordinary window for the hero it needs.
    /// </summary>
    public TimeSpan FocusCandidateMaxAge { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Focus-hero recordings (#433) that may wait for upload past a full upload backlog: a focus
    /// replay is recorded while fewer than this many focus recordings wait, so focus videos keep
    /// coming when ordinary recordings fill <see cref="MaxWaitingRecordings"/>. The uploader sends them right
    /// after the requests. Zero turns the lane off.
    /// </summary>
    public int FocusRecordingLane { get; set; } = 8;

    /// <summary>
    /// The share of <see cref="MapCooldown"/> and <see cref="RankCooldown"/> a focus-hero video
    /// (#433) is paced with, 0 to 1, so it publishes a little sooner than an ordinary video. The
    /// featured-hero cooldown and the roster check apply to it in full.
    /// </summary>
    public double FocusCooldownShare { get; set; } = 0.5;

    /// <summary>A copy for pacing a focus-hero video: the map and rank cooldowns scaled by <see cref="FocusCooldownShare"/>.</summary>
    public ReplayMediaPolicySettings ForFocusPacing()
    {
        double share = double.IsFinite(FocusCooldownShare)
            ? Math.Clamp(FocusCooldownShare, 0, 1)
            : 1;
        var copy = (ReplayMediaPolicySettings)MemberwiseClone();
        copy.MapCooldown = TimeSpan.FromTicks((long)(MapCooldown.Ticks * share));
        copy.RankCooldown = TimeSpan.FromTicks((long)(RankCooldown.Ticks * share));
        return copy;
    }
}
