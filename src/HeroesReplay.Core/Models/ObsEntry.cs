using System.Collections.Generic;

namespace HeroesReplay.Core.Models
{
    public class ObsEntry
    {
        public string ReplayId { get; set; }
        public string TwitchLogin { get; set; }
        public string RecordingDirectory { get; set; }
        public string Rank { get; set; }
        public string Map { get; set; }
        public string TeamBans { get; set; }
        public string GameType { get; set; }
    }
}
