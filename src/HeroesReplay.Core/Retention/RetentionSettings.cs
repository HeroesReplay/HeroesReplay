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
}
