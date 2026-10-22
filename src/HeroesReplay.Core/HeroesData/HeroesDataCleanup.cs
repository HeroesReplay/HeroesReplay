using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Retention;

namespace HeroesReplay.Core.HeroesData;

/// <summary>
/// The heroes-data2 cache (#493). Each refresh extracts a build's files next to the older ones
/// (<c>data\herodata_98348.json</c> beside <c>herodata_98304.json</c>) and the game data reads the
/// newest, so an older build's files and every downloaded zip only take space. The newest
/// <c>HeroesToolChest:RetainedVersions</c> builds stay.
/// </summary>
public static class HeroesDataCleanup
{
    public const string PrunerName = "heroes-data";

    /// <summary>A zip younger than this may still be downloading or extracting.</summary>
    public static readonly TimeSpan ZipGrace = TimeSpan.FromHours(1);

    public static IPruner Pruner(AppSettings settings) =>
        new Pruner(
            PrunerName,
            "heroes-data2 files of builds older than the newest HeroesToolChest:RetainedVersions, and downloaded zips once extracted.",
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
        if (string.IsNullOrWhiteSpace(heroesData) || !Directory.Exists(heroesData))
        {
            return plan;
        }

        var root = new DirectoryInfo(heroesData);
        foreach (FileInfo zip in root.GetFiles("*.zip"))
        {
            if (zip.LastWriteTimeUtc < (utcNow - ZipGrace).UtcDateTime)
            {
                plan.Add(
                    PruneItem.ForFile(zip, "downloaded heroes-data2 zip: its files are extracted")
                );
            }
        }

        List<(FileInfo File, int Build)> documents = root.GetFiles(
                "*.json",
                SearchOption.AllDirectories
            )
            .Where(file =>
                GameData.IsHeroesData2Document(file.FullName)
                || file.Name.Contains("gamestrings", StringComparison.OrdinalIgnoreCase)
            )
            .Select(file => (File: file, Build: GameData.BuildNumber(file.FullName)))
            .Where(item => item.Build > 0)
            .ToList();
        HashSet<int> kept = documents
            .Select(item => item.Build)
            .Distinct()
            .OrderByDescending(build => build)
            .Take(Math.Max(1, retainedVersions))
            .ToHashSet();
        foreach ((FileInfo file, int build) in documents.Where(item => !kept.Contains(item.Build)))
        {
            plan.Add(
                PruneItem.ForFile(
                    file,
                    $"heroes-data2 build {build} is older than the newest HeroesToolChest:RetainedVersions ({Math.Max(1, retainedVersions)}) builds"
                )
            );
        }

        return plan;
    }
}
