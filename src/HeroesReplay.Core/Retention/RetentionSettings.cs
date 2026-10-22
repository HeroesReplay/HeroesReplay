using System;

namespace HeroesReplay.Core.Retention;

public class RetentionSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Context folders with nothing left to upload are removed once untouched for this many days:
    /// uploaded ones, ones whose recording was retired (<c>youtube-entry-expired.json</c>), and
    /// ones whose entry has no recording (#489).
    /// </summary>
    public int VideoKeepDays { get; set; } = 3;

    /// <summary>
    /// Dry-run recordings (a context with <c>youtube-dry-run.json</c>; one whose entry still waits
    /// counts only while <c>YouTube:DryRun</c> is on) are removed after this many days, then their
    /// folder (#489). A recording that waits for its insert in a live run is never removed for its
    /// age here: the pending-bytes gate and the uploader's publication window decide it.
    /// </summary>
    public int VideoMaxAgeDays { get; set; } = 7;

    /// <summary>
    /// A recording with no YouTube entry is removed once it has not been written for this long.
    /// OBS writes the file while it records, and the spectator writes the entry seconds after it
    /// stops, so a recording this quiet can no longer be published (#204, #212).
    /// </summary>
    public TimeSpan UnpublishedGrace { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// With <c>YouTube:DryRun</c> on, a recording is never sent. Once the uploader has written its
    /// dry-run plan (<c>youtube-dry-run.json</c>) and the mp4 is older than this, the youtube role
    /// deletes the mp4. Clips, <c>end.png</c>, and the json files stay. Zero turns it off: the
    /// base and production settings leave it off, dev uses 2 days (#317).
    /// </summary>
    public TimeSpan DryRunRecordingMaxAge { get; set; }

    /// <summary>Played .StormReplay files older than this are removed. They are much smaller than videos.</summary>
    public int ReplayKeepDays { get; set; } = 30;

    /// <summary>
    /// An upload attempt folder in a terminal state (uploaded, simulated, disabled, never
    /// recorded, or its recording gone) is removed once its manifest is this old (#492). One in
    /// <c>UploadPending</c>, <c>Uploading</c> or <c>AmbiguousUpload</c> never is.
    /// </summary>
    public int AttemptKeepDays { get; set; } = 14;

    /// <summary>A replay lease (<c>Data\leases</c>) is removed once this old (#492). Nothing reads one after its replay is taken.</summary>
    public int LeaseKeepDays { get; set; } = 2;

    /// <summary>
    /// Settled predictions in <c>prediction-ledger.json</c> and past publication reservations
    /// older than this are dropped by the process that owns each file, when it next loads or
    /// sweeps it (#492). Open predictions and future slots always stay.
    /// </summary>
    public int LedgerKeepDays { get; set; } = 30;

    /// <summary>
    /// <c>DurableFile</c> moves an unreadable file aside as <c>&lt;file&gt;.corrupt-&lt;ticks&gt;</c>.
    /// The newest <see cref="CorruptKeepCount"/> per file stay, each at most this many days (#492).
    /// </summary>
    public int CorruptKeepDays { get; set; } = 30;

    public int CorruptKeepCount { get; set; } = 3;

    /// <summary>
    /// A cached replay that will never play goes after this many days (#493): quarantined, below
    /// the patch floor, or an ordinary replay that was downloaded and never played (ordinary
    /// replays play newest first). A viewer request that waits is never removed.
    /// </summary>
    public int UnplayedReplayKeepDays { get; set; } = 14;

    /// <summary>
    /// A retained client build (<c>Data\Clients\Base*</c>) below the <c>Spectate:MinimumGameVersion</c>
    /// patch line goes once its folder is this old (#493). No replay below the line can be played
    /// or requested, and the live build is never removed.
    /// </summary>
    public int ClientBuildKeepDays { get; set; } = 7;

    public int EffectiveUnplayedReplayKeepDays =>
        UnplayedReplayKeepDays > 0 ? UnplayedReplayKeepDays : 14;
    public int EffectiveClientBuildKeepDays => ClientBuildKeepDays > 0 ? ClientBuildKeepDays : 7;

    public int EffectiveAttemptKeepDays => AttemptKeepDays > 0 ? AttemptKeepDays : 14;
    public int EffectiveLeaseKeepDays => LeaseKeepDays > 0 ? LeaseKeepDays : 2;
    public int EffectiveLedgerKeepDays => LedgerKeepDays > 0 ? LedgerKeepDays : 30;
    public int EffectiveCorruptKeepDays => CorruptKeepDays > 0 ? CorruptKeepDays : 30;
    public int EffectiveCorruptKeepCount => CorruptKeepCount > 0 ? CorruptKeepCount : 3;
}
