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

    /// <summary>Played .StormReplay files older than this are removed. They are much smaller than videos.</summary>
    public int ReplayKeepDays { get; set; } = 30;
}
