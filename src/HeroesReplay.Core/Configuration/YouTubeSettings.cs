using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Configuration;

public class YouTubeSettings
{
    public bool Enabled { get; set; }

    /// <summary>
    /// When true, the uploader records the title and file size and does not call YouTube.
    /// Production sets this to false.
    /// </summary>
    public bool DryRun { get; set; } = true;

    public int ReadyStableReads { get; set; } = 5;
    public int ReadyPollMilliseconds { get; set; } = 2000;
    public bool UploadRequestedReplays { get; set; }
    public string ApiKey { get; set; }
    public string ChannelId { get; set; }
    public string EntryFileName { get; set; }
    public string CategoryId { get; set; }
    public string PrivacyStatus { get; set; }
    public string EntryFileNameUploaded { get; set; }
}
