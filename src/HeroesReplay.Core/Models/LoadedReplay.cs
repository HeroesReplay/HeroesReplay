using System.IO;
using Heroes.ReplayParser;

/// <summary>
/// The StormReplay is a wrapper which links a raw replay file on disk to an in-memory parsed version of that file
/// </summary>
namespace HeroesReplay.Core.Models;

public class LoadedReplay
{
    public int? ReplayId { get; set; }

    /// <summary>True when this replay id already has a video on the YouTube channel.</summary>
    public bool AlreadyOnYouTube { get; set; }
    public Replay Replay { get; set; }
    public FileInfo FileInfo { get; set; }
    public HeroesProfileReplay HeroesProfileReplay { get; set; }
    public RewardQueueItem RewardQueueItem { get; set; }
}
