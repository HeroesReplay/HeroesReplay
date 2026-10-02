using System;

namespace HeroesReplay.Core.Spectating;

public static class EndScreenHold
{
    public static readonly TimeSpan Mvp = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan UnconfirmedCap = TimeSpan.FromSeconds(35);

    public static TimeSpan Duration(bool mvpSeen, TimeSpan configured)
    {
        if (mvpSeen)
        {
            return Mvp;
        }

        if (configured <= TimeSpan.Zero || configured > UnconfirmedCap)
        {
            return UnconfirmedCap;
        }

        return configured;
    }
}
