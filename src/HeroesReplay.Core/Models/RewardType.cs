using System;

namespace HeroesReplay.Core.Models
{
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
}
