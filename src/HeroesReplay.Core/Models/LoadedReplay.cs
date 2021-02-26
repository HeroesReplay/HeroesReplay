using Heroes.ReplayParser;
using System.IO;

namespace HeroesReplay.Core.Models
{
    /// <summary>
    /// The StormReplay is a wrapper which links a raw replay file on disk to an in-memory parsed version of that file
    /// </summary>
    public class LoadedReplay
    {
        public int? ReplayId { get; set; }
        public Replay Replay { get; set; }
        public FileInfo FileInfo { get; set; }
        public HeroesProfileReplay HeroesProfileReplay { get; set; }
        public RewardQueueItem RewardQueueItem { get; set; }
    }
}