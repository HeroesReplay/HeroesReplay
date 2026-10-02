using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.HeroesProfile;

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

    /// <summary>
    /// The main patch is the first two numbers. 2.57.0.98285 and 2.57.0.98304 are both 2.57.
    /// </summary>
    public static string PatchLine(string version)
    {
        string[] parts = Segments(version);
        if (parts == null || parts.Length < 2)
        {
            return null;
        }

        if (!IsDigits(parts[0]) || !IsDigits(parts[1]))
        {
            return null;
        }

        return parts[0] + "." + parts[1];
    }

    public static bool SamePatch(string left, string right)
    {
        string line = PatchLine(left);
        return line != null && string.Equals(line, PatchLine(right), StringComparison.Ordinal);
    }

    private static string[] Segments(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        string normalized = version
            .Trim()
            .Replace(',', '.')
            .Replace(" ", "", StringComparison.Ordinal);
        if (normalized.Length == 0)
        {
            return null;
        }

        return normalized.Split('.');
    }

    private static bool IsDigits(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (character < '0' || character > '9')
            {
                return false;
            }
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
