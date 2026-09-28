using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.Services.HeroesProfile;

public static class GameVersionOrder
{
    public static bool Allows(
        string version,
        IEnumerable<string> exactVersions,
        string minimumVersion
    )
    {
        if (!string.IsNullOrWhiteSpace(minimumVersion))
        {
            return IsAtLeast(version, minimumVersion);
        }

        if (
            exactVersions != null
            && exactVersions.Any(versionName => !string.IsNullOrWhiteSpace(versionName))
        )
        {
            return exactVersions.Contains(version);
        }

        return true;
    }

    public static bool IsAtLeast(string version, string minimum)
    {
        if (string.IsNullOrWhiteSpace(minimum))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        int[] left = Parts(version);
        int[] right = Parts(minimum);
        int count = Math.Max(left.Length, right.Length);
        for (int i = 0; i < count; i++)
        {
            int actual = i < left.Length ? left[i] : 0;
            int floor = i < right.Length ? right[i] : 0;
            if (actual == floor)
            {
                continue;
            }

            return actual > floor;
        }

        return true;
    }

    private static int[] Parts(string version)
    {
        return version
            .Split('.')
            .Select(part => int.TryParse(part, out int number) ? number : 0)
            .ToArray();
    }
}
