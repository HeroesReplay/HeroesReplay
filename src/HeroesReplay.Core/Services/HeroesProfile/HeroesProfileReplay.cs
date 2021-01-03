using System;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class HeroesProfileReplay
    {
        [JsonPropertyName("replayID")]
        public int Id { get; set; }

        [JsonPropertyName("hotsapi_replayID")]
        public int? HotsApiReplayId { get; set; }

        [JsonPropertyName("region")]
        public string Region { get; set; }

        [JsonPropertyName("url")]
        public Uri Url { get; set; }

        [JsonPropertyName("fingerprint")]
        public string Fingerprint { get; set; }

        [JsonPropertyName("checked_hotsapi")]
        public int? CheckedHotsApi { get; set; }

        [JsonPropertyName("parsed")]
        public int? Parsed { get; set; }

        [JsonPropertyName("valid")]
        public int? Valid { get; set; }

        [JsonPropertyName("deleted")]
        public string Deleted { get; set; }

        [JsonPropertyName("game_type")]
        public string GameType { get; set; }

        [JsonPropertyName("game_version")]
        public string GameVersion { get; set; }
    }
}

