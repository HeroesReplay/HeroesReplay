using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace HeroesReplay.Core.Shared
{
    public static class EnumerableExtensions
    {
        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> items) => items.OrderBy(i => Guid.NewGuid());

        public static IEnumerable<TSource> Interleave<TSource>(this IEnumerable<TSource> source1, IEnumerable<TSource> source2)
        {
            using (var enumerator1 = source1.GetEnumerator())
            {
                using (var enumerator2 = source2.GetEnumerator())
                {
                    bool continue1;
                    bool continue2;

                    do
                    {

                        if (continue1 = enumerator1.MoveNext())
                        {
                            yield return enumerator1.Current;
                        }

                        if (continue2 = enumerator2.MoveNext())
                        {
                            yield return enumerator2.Current;
                        }

                    }
                    while (continue1 || continue2);
                }
            }
        }
    }
}
