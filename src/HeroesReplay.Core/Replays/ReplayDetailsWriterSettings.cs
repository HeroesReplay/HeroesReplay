namespace HeroesReplay.Core.Replays;

public class ReplayDetailsWriterSettings
{
    public bool Enabled { get; set; }
    public bool Bans { get; set; }
    public bool GameType { get; set; }
    public bool Requestor { get; set; }

    /// <summary>A last "Patch: 2.57.0.98348" line with the replay's client build.</summary>
    public bool Patch { get; set; }
}
