using System;
using System.Text.Json.Serialization;
using HeroesReplay.Core.HeroesProfile;

namespace HeroesReplay.Core.Requests;

[Serializable]
public class RewardQueueItem
{
    [JsonPropertyName("Request")]
    public RewardRequest Request { get; set; }

    [JsonPropertyName("HeroesProfileReplay")]
    public HeroesProfileReplay HeroesProfileReplay { get; set; }

    public RewardQueueItem() { }

    public RewardQueueItem(RewardRequest request, HeroesProfileReplay replay)
    {
        HeroesProfileReplay = replay;
        Request = request;
    }
}
