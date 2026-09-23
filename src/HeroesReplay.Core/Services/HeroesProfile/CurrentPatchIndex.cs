using System;

namespace HeroesReplay.Core.Services.HeroesProfile;

/// <summary>
/// Smallest replay id that still uses the same game version as the latest replay.
/// Versions only move forward as ids increase.
/// </summary>
public static class CurrentPatchIndex
{
    public readonly record struct Row(int Id, string Version);

    public static int FindFirst(int maxId, string version, Func<int, Row?> firstAfter)
    {
        if (maxId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxId));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A game version is required.", nameof(version));
        }

        if (firstAfter == null)
        {
            throw new ArgumentNullException(nameof(firstAfter));
        }

        int lo = 1;
        int hi = maxId;
        int answer = maxId;
        for (int guard = 0; lo <= hi && guard < 40; guard++)
        {
            int mid = lo + ((hi - lo) / 2);
            int after = Math.Max(1, mid - 1);
            Row? row = firstAfter(after);
            if (row == null || row.Value.Id <= after)
            {
                hi = mid - 1;
                continue;
            }

            if (string.Equals(row.Value.Version, version, StringComparison.OrdinalIgnoreCase))
            {
                answer = row.Value.Id;
                hi = row.Value.Id - 1;
            }
            else
            {
                lo = row.Value.Id + 1;
            }
        }

        return answer;
    }
}
