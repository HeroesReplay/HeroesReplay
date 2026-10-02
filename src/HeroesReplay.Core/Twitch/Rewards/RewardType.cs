using System;

namespace HeroesReplay.Core.Twitch.Rewards;

/// <summary>
/// Rewards combine a mode with Map and Rank (<c>QM | Map | Rank</c>), so each member needs its own bit.
/// Values are saved by name, not number.
/// </summary>
[Flags]
public enum RewardType
{
    ReplayId = 0,

    ARAM = 1,
    QM = 2,
    UD = 4,
    SL = 8,

    Map = 16,
    Rank = 32,
}
