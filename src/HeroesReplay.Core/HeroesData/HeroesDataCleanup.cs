using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Retention;

namespace HeroesReplay.Core.HeroesData;

/// <summary>
/// The heroes-data2 cache (#493). A refresh publishes each new build in whole as
/// <c>builds\&lt;build&gt;</c> beside the older ones (the first download, before #493, sits flat in
/// <c>data</c> and <c>gamestrings</c>), and the game data reads the newest. The newest
/// <c>HeroesToolChest:RetainedVersions</c> builds stay; older builds, downloaded zips and staging
/// folders a crashed download left go. Nothing is planned while a process holds the heroes-data2
/// lock (a refresh or a load). A running role loaded its build into memory at start and never
/// reads the files again, and the build before the newest stays too.
/// </summary>
public static class HeroesDataCleanup
{
    public const string PrunerName = "heroes-data";

    /// <summary>A zip or staging folder younger than this may belong to a download under way.</summary>
    public static readonly TimeSpan DownloadGrace = TimeSpan.FromHours(1);

    public static IPruner Pruner(AppSettings settings) =>
        new Pruner(
            PrunerName,
            "heroes-data2 builds older than the newest HeroesToolChest:RetainedVersions, downloaded zips, and staging folders left by a crashed download. Nothing while a role refreshes or loads it.",
            now =>
                string.IsNullOrWhiteSpace(settings?.Location?.DataDirectory)
                    ? new PrunePlan(PrunerName)
                    : Plan(
                        settings.HeroesDataPath,
                        settings.HeroesToolChest?.RetainedVersions ?? 2,
                        now
                    )
        );

    /// <summary>What the pruner removes now. Writes nothing.</summary>
    public static PrunePlan Plan(string heroesData, int retainedVersions, DateTimeOffset utcNow)
    {
        var plan = new PrunePlan(PrunerName);
        if (
            string.IsNullOrWhiteSpace(heroesData)
            || !Directory.Exists(heroesData)
            || GameData.IsBusy(heroesData)
        )
        {
            return plan;
        }

        var root = new DirectoryInfo(heroesData);
        DateTime before = (utcNow - DownloadGrace).UtcDateTime;
        foreach (DirectoryInfo staging in root.GetDirectories(GameData.StagingPrefix + "*"))
        {
            if (staging.LastWriteTimeUtc < before)
            {
                plan.Add(
                    PruneItem.ForDirectory(
                        staging,
                        "staging folder of a heroes-data2 download that never finished"
                    )
                );
            }
        }

        foreach (FileInfo zip in root.GetFiles("*.zip"))
        {
            if (zip.LastWriteTimeUtc < before)
            {
                plan.Add(
                    PruneItem.ForFile(zip, "downloaded heroes-data2 zip: its files are extracted")
                );
            }
        }

        string builds =
            Path.Combine(root.FullName, GameData.BuildsFolderName) + Path.DirectorySeparatorChar;
        List<(FileInfo File, int Build)> documents = root.GetFiles(
                "*.json",
                SearchOption.AllDirectories
            )
            .Where(file =>
                !file.FullName.Contains(
                    Path.DirectorySeparatorChar + GameData.StagingPrefix,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Where(file =>
                GameData.IsHeroesData2Document(file.FullName)
                || file.Name.Contains("gamestrings", StringComparison.OrdinalIgnoreCase)
            )
            .Select(file => (File: file, Build: GameData.BuildNumber(file.FullName)))
            .Where(item => item.Build > 0)
            .ToList();
        int keep = Math.Max(1, retainedVersions);
        HashSet<int> kept = documents
            .Select(item => item.Build)
            .Distinct()
            .OrderByDescending(build => build)
            .Take(keep)
            .ToHashSet();
        var plannedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((FileInfo file, int build) in documents.Where(item => !kept.Contains(item.Build)))
        {
            string reason =
                $"heroes-data2 build {build} is older than the newest HeroesToolChest:RetainedVersions ({keep}) builds";
            if (file.FullName.StartsWith(builds, StringComparison.OrdinalIgnoreCase))
            {
                // A published build goes in whole, as it came.
                string folder = Path.Combine(
                    builds,
                    file.FullName.Substring(builds.Length).Split(Path.DirectorySeparatorChar)[0]
                );
                if (plannedFolders.Add(folder))
                {
                    plan.Add(PruneItem.ForDirectory(new DirectoryInfo(folder), reason));
                }

                continue;
            }

            plan.Add(PruneItem.ForFile(file, reason));
        }

        return plan;
    }
}
