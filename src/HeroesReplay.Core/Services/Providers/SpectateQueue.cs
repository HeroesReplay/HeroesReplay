using System.Collections.Generic;

namespace HeroesReplay.Core.Services.Providers;

/// <summary>
/// Files the downloader must not treat as a full spectate queue.
/// A below-floor, quarantined, or deferred replay stays on disk and is not waiting to be played.
/// </summary>
public static class SpectateQueue
{
    public const string SpectatedFileName = "spectated-ids.txt";
    public const string BelowFloorFileName = "below-floor-ids.txt";
    public const string QuarantineFileName = "quarantine-ids.txt";
    public const string DeferredFileName = "deferred-replays.txt";

    public static int CountWaiting(IEnumerable<int> onDisk, IEnumerable<int> notWaiting, int stopAt)
    {
        var skip = new HashSet<int>();
        if (notWaiting != null)
        {
            foreach (int id in notWaiting)
            {
                skip.Add(id);
            }
        }

        int waiting = 0;
        if (onDisk == null)
        {
            return 0;
        }

        foreach (int id in onDisk)
        {
            if (skip.Contains(id))
            {
                continue;
            }

            waiting++;
            if (stopAt > 0 && waiting >= stopAt)
            {
                return waiting;
            }
        }

        return waiting;
    }

    /// <summary>
    /// A spectated line is the id. A deferred line is the id, then the unix time.
    /// </summary>
    public static bool TryParseId(string line, out int id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string trimmed = line.Trim();
        int end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]))
        {
            end++;
        }

        return int.TryParse(trimmed.Substring(0, end), out id);
    }
}
