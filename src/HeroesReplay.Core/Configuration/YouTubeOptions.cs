using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Configuration
{
    public class YouTubeOptions
    {
        public bool Enabled { get; set; }
        public string ApiKey { get; set; }
        public string ChannelId { get; set; }
        public string EntryFileName { get; set; }
        public string CategoryId { get; set; }
        public string SecretsFileName { get; set; }
    }
}