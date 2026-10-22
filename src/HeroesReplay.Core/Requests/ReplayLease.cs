using System;
using System.Globalization;
using System.IO;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Requests;

/// <summary>
/// A taken replay is leased on disk before it is returned. A restart reads the same file.
/// A missing Heroes build is not leased. Nothing moves a lease past <c>Leased</c> or reads it
/// after the take, so every played replay left one (1,251 on the stream PC); the housekeeping pass
/// removes them after <c>Retention:LeaseKeepDays</c> (#492).
/// </summary>
public static class ReplayLease
{
    public const string PrunerName = "leases";

    public static IPruner Pruner(AppSettings settings) =>
        new Pruner(
            PrunerName,
            "Replay lease files (Data\\leases) after Retention:LeaseKeepDays: a lease is only read when its replay is taken.",
            now =>
                Plan(
                    settings?.Location?.DataDirectory,
                    (settings?.Retention ?? new RetentionSettings()).EffectiveLeaseKeepDays,
                    now
                )
        );

    /// <summary>The lease files older than <paramref name="keepDays"/>. Writes nothing.</summary>
    public static PrunePlan Plan(string dataDirectory, int keepDays, DateTimeOffset utcNow)
    {
        var plan = new PrunePlan(PrunerName);
        string folder = string.IsNullOrWhiteSpace(dataDirectory)
            ? null
            : Path.Combine(dataDirectory, "leases");
        if (folder == null || !Directory.Exists(folder))
        {
            return plan;
        }

        DateTime before = utcNow.AddDays(-Math.Max(1, keepDays)).UtcDateTime;
        foreach (FileInfo lease in new DirectoryInfo(folder).GetFiles("*.txt"))
        {
            if (lease.LastWriteTimeUtc < before)
            {
                plan.Add(
                    PruneItem.ForFile(
                        lease,
                        $"lease older than Retention:LeaseKeepDays ({keepDays}); its replay was taken long ago"
                    )
                );
            }
        }

        return plan;
    }

    public static bool Take(string path, int replayId)
    {
        if (replayId <= 0 || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!WorkEnvelope.TryTransition(WorkState.Ready, WorkState.Leased))
        {
            return false;
        }

        string id = replayId.ToString(CultureInfo.InvariantCulture);
        if (IsLease(DurableFile.ReadOrAside(path), id))
        {
            return true;
        }

        string body = "state=Leased" + Environment.NewLine + "replay=" + id + Environment.NewLine;
        DurableFile.Replace(path, body);
        return IsLease(DurableFile.ReadOrAside(path), id);
    }

    public static string PathFor(string dataDirectory, int replayId)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory) || replayId <= 0)
        {
            return null;
        }

        return Path.Combine(
            dataDirectory,
            "leases",
            replayId.ToString(CultureInfo.InvariantCulture) + ".txt"
        );
    }

    private static bool IsLease(string text, string id)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text.Contains("state=Leased", StringComparison.Ordinal)
            && text.Contains("replay=" + id, StringComparison.Ordinal);
    }
}
