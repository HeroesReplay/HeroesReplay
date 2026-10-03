using System;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Requests;

public class RewardRequest
{
    public Guid RedemptionId { get; set; }

    /// <summary>The channel-point reward. Twitch needs it to mark the redemption FULFILLED.</summary>
    public Guid RewardId { get; set; }

    /// <summary>The channel's Twitch user id, from the redemption event.</summary>
    public string BroadcasterId { get; set; }
    public string RewardTitle { get; set; }
    public string Login { get; set; }
    public int? ReplayId { get; set; }

    /// <summary>
    /// The player to follow, Name#1234, from the ReplayId reward or <c>spectate file --player</c>.
    /// Resolved to an observe slot from the parsed replay by
    /// <see cref="PlayerPriorityRequest.PlayerIndex"/>.
    /// </summary>
    public string BattleTag { get; set; }
    public GameRank? Rank { get; set; }
    public string Map { get; set; }
    public GameType? GameType { get; set; }
    public bool RecordAndUpload { get; set; }

    public RewardRequest() { }

    public RewardRequest(
        string login,
        Guid redemptionId,
        string rewardTitle,
        int? replayId,
        GameRank? rank,
        string map,
        GameType? gameMode
    )
    {
        RedemptionId = redemptionId;
        Login = login;
        RewardTitle = rewardTitle;
        ReplayId = replayId;
        Rank = rank;
        Map = map;
        GameType = gameMode;
    }
}
