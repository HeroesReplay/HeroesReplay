namespace HeroesReplay.Core.Configuration;

public class RetentionSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Uploaded recordings and their context folders older than this are removed.</summary>
    public int VideoKeepDays { get; set; } = 3;

    /// <summary>Recordings that never finished uploading are removed after this many days.</summary>
    public int VideoMaxAgeDays { get; set; } = 7;

    /// <summary>Played .StormReplay files older than this are removed. They are much smaller than videos.</summary>
    public int ReplayKeepDays { get; set; } = 30;
}
