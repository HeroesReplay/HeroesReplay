using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.YouTube;

/// <summary>
/// Why each pending recording waits, so the uploader logs a wait once: when the recording first
/// starts waiting and when its reason changes, not on every pass. The publication summary is
/// logged at most every <see cref="SummaryInterval"/>.
/// </summary>
public sealed class UploadWaitLog
{
    public static readonly TimeSpan SummaryInterval = TimeSpan.FromMinutes(5);

    private readonly object gate = new();
    private readonly Dictionary<string, string> reasons = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? summarizedAt;

    /// <summary>
    /// Records that <paramref name="path"/> waits for <paramref name="reason"/>. True when the
    /// caller should log it: the first wait, or a different reason than last time.
    /// </summary>
    public bool Wait(string path, string reason)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        string key = Key(path);
        reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim();
        lock (gate)
        {
            if (
                reasons.TryGetValue(key, out string previous)
                && string.Equals(previous, reason, StringComparison.Ordinal)
            )
            {
                return false;
            }

            reasons[key] = reason;
            return true;
        }
    }

    /// <summary><paramref name="path"/> no longer waits: it was sent, or it is gone.</summary>
    public void Clear(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (gate)
        {
            reasons.Remove(Key(path));
        }
    }

    /// <summary>Forgets every recording that is no longer pending.</summary>
    public void Retain(IEnumerable<string> pending)
    {
        var keep = new HashSet<string>(
            (pending ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Key),
            StringComparer.OrdinalIgnoreCase
        );
        lock (gate)
        {
            foreach (string key in reasons.Keys.Where(key => !keep.Contains(key)).ToList())
            {
                reasons.Remove(key);
            }
        }
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return reasons.Count;
            }
        }
    }

    /// <summary>The reason most recordings wait for, or null when none waits.</summary>
    public string Main()
    {
        lock (gate)
        {
            return reasons
                .Values.GroupBy(reason => reason, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group.Key)
                .FirstOrDefault();
        }
    }

    /// <summary><c>insert-cap 17, publication-full 3</c>, or <c>none</c>.</summary>
    public string Describe()
    {
        lock (gate)
        {
            if (reasons.Count == 0)
            {
                return "none";
            }

            return string.Join(
                ", ",
                reasons
                    .Values.GroupBy(reason => reason, StringComparer.Ordinal)
                    .OrderByDescending(group => group.Count())
                    .ThenBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => group.Key + " " + group.Count())
            );
        }
    }

    /// <summary>True at most once per <see cref="SummaryInterval"/>; the first call is true.</summary>
    public bool SummaryDue(DateTimeOffset now)
    {
        lock (gate)
        {
            if (summarizedAt is DateTimeOffset last && now >= last && now - last < SummaryInterval)
            {
                return false;
            }

            summarizedAt = now;
            return true;
        }
    }

    private static string Key(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return path;
        }
    }
}
