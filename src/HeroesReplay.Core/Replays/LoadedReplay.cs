using System.IO;
using Heroes.ReplayParser;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.HeroFocus.Picking;
using HeroesReplay.Core.Requests;

/// <summary>
/// The StormReplay is a wrapper which links a raw replay file on disk to an in-memory parsed version of that file
/// </summary>
namespace HeroesReplay.Core.Replays;

public class LoadedReplay
{
    public int? ReplayId { get; set; }

    /// <summary>True when this replay id already has a video on the YouTube channel.</summary>
    public bool AlreadyOnYouTube { get; set; }

    /// <summary>
    /// Null until a pre-launch policy snapshot is applied. False does not start an owned recording.
    /// </summary>
    public bool? PolicyAllowsRecording { get; set; }

    /// <summary>
    /// Null until publication is decided. False does not write a YouTube entry.
    /// </summary>
    public bool? PolicyAllowsPublication { get; set; }
    public Replay Replay { get; set; }
    public FileInfo FileInfo { get; set; }
    public HeroesProfileReplay HeroesProfileReplay { get; set; }
    public RewardQueueItem RewardQueueItem { get; set; }

    /// <summary>
    /// The player the cycle follows for this whole replay (#433), or null. Never set on a viewer
    /// request: a request's BattleTag decides its camera.
    /// </summary>
    public FocusPick FocusPick { get; set; }
}
