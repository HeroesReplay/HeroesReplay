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

    /// <summary>
    /// Failed download attempts, or why the request was given up (#351). Null for a request
    /// whose download has not failed. Older builds ignore it.
    /// </summary>
    [JsonPropertyName("Download")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RequestDownload Download { get; set; }

    public RewardQueueItem() { }

    public RewardQueueItem(RewardRequest request, HeroesProfileReplay replay)
    {
        HeroesProfileReplay = replay;
        Request = request;
    }

    /// <summary>
    /// The same viewer request: the same redemption when both have one, otherwise the same
    /// viewer, reward and replay.
    /// </summary>
    public bool IsSameRequest(RewardQueueItem other)
    {
        if (other == null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        Guid mine = Request?.RedemptionId ?? Guid.Empty;
        Guid theirs = other.Request?.RedemptionId ?? Guid.Empty;
        if (mine != Guid.Empty || theirs != Guid.Empty)
        {
            return mine == theirs;
        }

        return string.Equals(
                Request?.Login,
                other.Request?.Login,
                StringComparison.OrdinalIgnoreCase
            )
            && string.Equals(
                Request?.RewardTitle,
                other.Request?.RewardTitle,
                StringComparison.Ordinal
            )
            && Request?.ReplayId == other.Request?.ReplayId
            && HeroesProfileReplay?.Id == other.HeroesProfileReplay?.Id;
    }
}
