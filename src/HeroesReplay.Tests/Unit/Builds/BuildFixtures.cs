using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Tests.Unit.Builds;

/// <summary>A small hero with talents named by tier and column, and guide builds over it.</summary>
internal static class BuildFixtures
{
    /// <summary>Talent ids are <c>T{tier}{column}</c>, buttons <c>B{tier}{column}</c>, names <c>Talent {tier}-{column}</c>.</summary>
    public static Hero Hero(
        string name = "Li-Ming",
        string unitId = "HeroWizard",
        params int[] columnsPerTier
    )
    {
        int[] columns = columnsPerTier.Length == 7 ? columnsPerTier : new[] { 3, 3, 3, 2, 3, 3, 4 };
        var talents = new List<HeroTalent>();
        for (int tier = 0; tier < 7; tier++)
        {
            for (int column = 1; column <= columns[tier]; column++)
            {
                talents.Add(
                    new HeroTalent(
                        $"T{tier}{column}",
                        $"B{tier}{column}",
                        tier,
                        column,
                        $"Talent {tier}-{column}",
                        null
                    )
                );
            }
        }

        return new Hero(name, unitId, null, null, talents: talents);
    }

    /// <summary>A build from seven mark strings: <c>y</c> recommended, <c>s</c> situational, <c>n</c> neither.</summary>
    public static GuideBuild Build(string name, string tag, params string[] marks) =>
        new()
        {
            Name = name,
            Tag = tag,
            Tiers = marks
                .Select(
                    (row, tier) =>
                        new GuideTier
                        {
                            Level = tier,
                            Recommended = Ids(row, tier, 'y'),
                            Situational = Ids(row, tier, 's'),
                        }
                )
                .ToArray(),
        };

    public static HeroGuide Guide(params GuideBuild[] builds) =>
        new()
        {
            Hero = "Li-Ming",
            UnitId = "HeroWizard",
            Slug = "li-ming",
            GuideUrl = "https://www.icy-veins.com/heroes/li-ming-build-guide",
            Builds = builds,
        };

    private static string[] Ids(string marks, int tier, char mark) =>
        marks
            .Select((value, column) => (value, column))
            .Where(pair => pair.value == mark)
            .Select(pair => $"T{tier}{pair.column + 1}")
            .ToArray();
}
