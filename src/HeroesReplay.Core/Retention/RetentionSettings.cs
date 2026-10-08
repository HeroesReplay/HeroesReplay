using System;

namespace HeroesReplay.Core.Retention;

public class RetentionSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Remotely uploaded recordings and their context folders older than this are removed.</summary>
    public int VideoKeepDays { get; set; } = 3;

    /// <summary>
    /// Recordings that were not uploaded to YouTube, including dry runs, are removed after this many days.
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
