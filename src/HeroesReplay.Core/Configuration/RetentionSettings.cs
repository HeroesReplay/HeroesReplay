namespace HeroesReplay.Core.Configuration;

public class RetentionSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Played replays and uploaded context folders older than this are removed.</summary>
    public int KeepDays { get; set; } = 1;

    /// <summary>Recordings that never finished uploading are removed after this many days.</summary>
    public int MaxAgeDays { get; set; } = 3;
}
