using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Spectator
{
    public static class Extensions
    {
        public static GameCriteria ToCriteria(this int count) => count switch
        {
            1 => GameCriteria.Kill,
            2 => GameCriteria.MultiKill,
            3 => GameCriteria.TripleKill,
            4 => GameCriteria.QuadKill,
            5 => GameCriteria.PentaKill,
            _ => throw new Exception("Unhandled kill count")
        };

        public static int ToKills(this GameCriteria criteria) => criteria switch
        {
            GameCriteria.Kill => 1,
            GameCriteria.MultiKill => 2,
            GameCriteria.TripleKill => 3,
            GameCriteria.QuadKill => 4,
            GameCriteria.PentaKill => 5,
            _ => throw new Exception($"Unhandled {nameof(GameCriteria)}")
        };

        public static IEnumerable<StormPlayer> Or(this IEnumerable<StormPlayer> selection, IEnumerable<StormPlayer> next)
        {
            return selection.Any() ? selection : next;
        }
    }
}