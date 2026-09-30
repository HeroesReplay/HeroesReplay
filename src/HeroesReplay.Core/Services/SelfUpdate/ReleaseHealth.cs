using System;

namespace HeroesReplay.Core.Services.SelfUpdate;

/// <summary>
/// The previous install stays until every role has been ready for the stabilization window.
/// </summary>
public static class ReleaseHealth
{
    public static readonly TimeSpan StabilizeFor = TimeSpan.FromMinutes(2);

    public static bool MayDiscardPrevious(bool allRolesReady, TimeSpan healthyFor)
    {
        if (!allRolesReady || healthyFor < TimeSpan.Zero)
        {
            return false;
        }

        return healthyFor >= StabilizeFor;
    }
}
