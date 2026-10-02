using System;

namespace HeroesReplay.Core.Twitch.Rewards;

[Flags]
public enum RewardType
{
    ReplayId,

    ARAM,
    QM,
    UD,
    SL,

    Map,
    Rank,
}
