using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Replays;

/// <summary>Replays waiting to be spectated, and how many of them are still fresh.</summary>
public readonly record struct WaitingReplays(int Waiting, int Fresh);

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

    /// <summary>
    /// Every waiting replay, and the fresh ones among them: still inside the media window.
    /// Only fresh replays count toward the download limit (#280). A waiting replay past its
    /// window stays on disk and is still played when nothing fresh is waiting. A null
    /// <paramref name="isFresh"/> counts every waiting replay as fresh.
    /// </summary>
    public static WaitingReplays CountWaiting(
        IEnumerable<int> onDisk,
        IEnumerable<int> notWaiting,
        Func<int, bool> isFresh
    )
    {
        if (onDisk == null)
        {
            return default;
        }

        var skip = new HashSet<int>();
        if (notWaiting != null)
        {
            foreach (int id in notWaiting)
            {
                skip.Add(id);
            }
        }

        int waiting = 0;
        int fresh = 0;
        foreach (int id in onDisk)
        {
            if (skip.Contains(id))
            {
                continue;
            }

            waiting++;
            if (isFresh == null || isFresh(id))
            {
                fresh++;
            }
        }

        return new WaitingReplays(waiting, fresh);
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
