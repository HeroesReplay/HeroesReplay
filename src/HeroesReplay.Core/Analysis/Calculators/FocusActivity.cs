using System;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Spectating;

namespace HeroesReplay.Core.Analysis.Calculators;

/// <summary>
/// What the proximity calculators share to tell activity from standing near something (#234).
/// </summary>
internal static class FocusActivity
{
    /// <summary>True when an alive hero of the other team is within <paramref name="maxDistance"/>.</summary>
    public static bool EnemyNear(
        ReplayTimeline timeline,
        Unit hero,
        Point point,
        int second,
        int maxDistance
    )
    {
        if (maxDistance <= 0)
        {
            return false;
        }

        foreach (Unit other in timeline.AliveHeroesAt(second))
        {
            if (
                other.Team != hero.Team
                && timeline.TryGetPoint(other, second, out Point otherPoint)
                && otherPoint.DistanceTo(point) <= maxDistance
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A remote-body hero (Abathur plays through his Symbiote) with no enemy hero near his body.
    /// Roaming and proximity focus skip it; kills, deaths, and fights still count.
    /// </summary>
    public static bool IdleRemoteBody(
        ReplayTimeline timeline,
        Unit hero,
        Point point,
        int second,
        SpectateSettings spectate
    )
    {
        string character = hero?.PlayerControlledBy?.Character;
        if (string.IsNullOrWhiteSpace(character) || spectate?.RemoteBodyHeroes == null)
        {
            return false;
        }

        if (
            !spectate.RemoteBodyHeroes.Any(name =>
                string.Equals(name, character, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return false;
        }

        return !EnemyNear(timeline, hero, point, second, spectate.MaxDistanceToEnemy);
    }
}
