using System;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// Smallest replay id that still uses the latest replay's patch.
/// Replay ids only move onto newer builds. A patch line is the first two numbers,
/// so 2.57.0.98285 and 2.57.0.98304 are the same patch.
/// </summary>
public static class CurrentPatchIndex
{
    public readonly record struct Row(int Id, string Version);

    public static int FindFirst(int maxId, string version, Func<int, Row?> firstAfter)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A game version is required.", nameof(version));
        }

        return Find(maxId, version, firstAfter, matchLine: false);
    }

    public static int FindFirstOnLine(int maxId, string version, Func<int, Row?> firstAfter)
    {
        if (GameVersionOrder.PatchLine(version) == null)
        {
            throw new ArgumentException(
                "A patch line needs a major and minor version.",
                nameof(version)
            );
        }

        return Find(maxId, version, firstAfter, matchLine: true);
    }

    private static int Find(int maxId, string version, Func<int, Row?> firstAfter, bool matchLine)
    {
        if (maxId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxId));
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

            bool matches = matchLine
                ? GameVersionOrder.SamePatch(row.Value.Version, version)
                : string.Equals(row.Value.Version, version, StringComparison.OrdinalIgnoreCase);
            if (matches)
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
