using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Models;

public class HeroesProfileReplayPage
{
    [JsonPropertyName("replays")]
    public List<HeroesProfileReplay> Replays { get; set; }

    [JsonPropertyName("next_after")]
    public int? NextAfter { get; set; }

    [JsonPropertyName("max_replay_id")]
    public int MaxReplayId { get; set; }
}
