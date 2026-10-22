using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Retention;

namespace HeroesReplay.Core.Replays;

/// <summary>
/// The replays the media pruner never retired (#493): it removes a cached replay only once it was
/// spectated (<c>spectated-ids.txt</c>). A quarantined (unreadable) replay, one below the patch
/// floor, and an ordinary replay that was downloaded and never played stayed forever: ordinary
/// replays play newest first, so an old one is only ever behind each day's downloads. They go
/// after <c>Retention:UnplayedReplayKeepDays</c>. A viewer request that has not played yet is
/// never removed, and nor is a replay taken for the current spectate (its lease). A request's
/// <c>.request.json</c> goes once its replay was played or is gone, after
/// <c>ReplayKeepDays</c>: the cache finds a request by replay id until it plays.
/// </summary>
public static class ReplayCacheCleanup
{
    public const string PrunerName = "replay-cache";

    public static IPruner Pruner(AppSettings settings) =>
        new Pruner(
            PrunerName,
            "Cached replays that will never play (quarantined, below the floor, or an ordinary replay never played) after Retention:UnplayedReplayKeepDays; request sidecars of played or gone replays after ReplayKeepDays. Waiting requests stay.",
            now => Plan(settings, now)
        );

    /// <summary>What the pruner removes now. Writes nothing.</summary>
    public static PrunePlan Plan(AppSettings settings, DateTimeOffset utcNow)
    {
        var plan = new PrunePlan(PrunerName);
        string data = settings?.Location?.DataDirectory;
        if (
            settings?.Retention?.Enabled == false
            || string.IsNullOrWhiteSpace(data)
            || settings.HeroesProfileApi == null
        )
        {
            return plan;
        }

        RetentionSettings retention = settings.Retention ?? new RetentionSettings();
        string separator = string.IsNullOrEmpty(settings.StormReplay?.Seperator)
            ? "_"
            : settings.StormReplay.Seperator;
        var rule = new Rule(
            PlayedReplayIds.Read(data),
            ReadIds(Path.Combine(data, SpectateQueue.QuarantineFileName)),
            ReadIds(Path.Combine(data, SpectateQueue.BelowFloorFileName)),
            separator,
            retention.EffectiveUnplayedReplayKeepDays,
            retention.ReplayKeepDays > 0 ? retention.ReplayKeepDays : 30,
            utcNow
        );
        string leases = Path.Combine(data, "leases");
        PlanReplays(settings.StandardReplayCachePath, leases, rule, request: false, plan);
        PlanReplays(settings.RequestedReplayCachePath, leases, rule, request: true, plan);
        PlanSidecars(
            settings.RequestedReplayCachePath,
            settings.StandardReplayCachePath,
            rule,
            plan
        );
        return plan;
    }

    private sealed record Rule(
        HashSet<int> Played,
        HashSet<int> Quarantined,
        HashSet<int> BelowFloor,
        string Separator,
        int UnplayedKeepDays,
        int ReplayKeepDays,
        DateTimeOffset UtcNow
    )
    {
        public DateTime UnplayedBefore => UtcNow.AddDays(-UnplayedKeepDays).UtcDateTime;
        public DateTime ReplayBefore => UtcNow.AddDays(-ReplayKeepDays).UtcDateTime;
    }

    private static void PlanReplays(
        string directory,
        string leases,
        Rule rule,
        bool request,
        PrunePlan plan
    )
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (FileInfo replay in new DirectoryInfo(directory).EnumerateFiles("*.StormReplay"))
        {
            if (
                !TryReplayId(replay.Name, rule.Separator, out int id)
                || rule.Played.Contains(id)
                || replay.LastWriteTimeUtc >= rule.UnplayedBefore
                || File.Exists(
                    Path.Combine(leases, id.ToString(CultureInfo.InvariantCulture) + ".txt")
                )
            )
            {
                continue;
            }

            string reason =
                rule.Quarantined.Contains(id) ? "quarantined (could not be parsed)"
                : rule.BelowFloor.Contains(id) ? "below the patch floor"
                : request ? null
                : "downloaded and never played: ordinary replays play newest first";
            if (reason == null)
            {
                // A viewer's request plays first come, first served; it waits however long it takes.
                continue;
            }

            plan.Add(
                PruneItem.ForFile(
                    replay,
                    $"{reason}, older than Retention:UnplayedReplayKeepDays ({rule.UnplayedKeepDays} days)"
                )
            );
        }
    }

    private static void PlanSidecars(string requests, string standard, Rule rule, PrunePlan plan)
    {
        if (string.IsNullOrWhiteSpace(requests) || !Directory.Exists(requests))
        {
            return;
        }

        foreach (
            FileInfo sidecar in new DirectoryInfo(requests).EnumerateFiles(
                "*" + CachedRequestReward.Extension
            )
        )
        {
            string replayName =
                sidecar.Name.Substring(
                    0,
                    sidecar.Name.Length - CachedRequestReward.Extension.Length
                ) + ".StormReplay";
            if (
                !TryReplayId(replayName, rule.Separator, out int id)
                || sidecar.LastWriteTimeUtc >= rule.ReplayBefore
            )
            {
                continue;
            }

            bool played = rule.Played.Contains(id);
            if (
                !played
                && (
                    HasReplay(requests, id, rule.Separator)
                    || HasReplay(standard, id, rule.Separator)
                )
            )
            {
                continue;
            }

            plan.Add(
                PruneItem.ForFile(
                    sidecar,
                    played
                        ? $"request of a played replay, older than Retention:ReplayKeepDays ({rule.ReplayKeepDays} days)"
                        : $"request whose replay is gone, older than Retention:ReplayKeepDays ({rule.ReplayKeepDays} days)"
                )
            );
        }
    }

    private static bool HasReplay(string directory, int id, string separator)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        string prefix = id.ToString(CultureInfo.InvariantCulture);
        return Directory
            .EnumerateFiles(directory, prefix + "*.StormReplay")
            .Any(path =>
                TryReplayId(Path.GetFileName(path), separator, out int found) && found == id
            );
    }

    private static HashSet<int> ReadIds(string path)
    {
        var ids = new HashSet<int>();
        try
        {
            if (!File.Exists(path))
            {
                return ids;
            }

            foreach (string line in File.ReadLines(path))
            {
                if (int.TryParse(line?.Trim(), out int id) && id > 0)
                {
                    ids.Add(id);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ids;
        }

        return ids;
    }

    private static bool TryReplayId(string fileName, string separator, out int replayId)
    {
        replayId = 0;
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrEmpty(separator))
        {
            return false;
        }

        string head = fileName.Split(separator)[0];
        if (head.EndsWith(".StormReplay", StringComparison.OrdinalIgnoreCase))
        {
            head = head.Substring(0, head.Length - ".StormReplay".Length);
        }

        return int.TryParse(head, out replayId) && replayId > 0;
    }
}
