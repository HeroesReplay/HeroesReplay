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

    /// <summary>Played .StormReplay files older than this are removed. They are much smaller than videos.</summary>
    public int ReplayKeepDays { get; set; } = 30;
}
