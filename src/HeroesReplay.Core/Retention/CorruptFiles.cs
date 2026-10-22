using System;
using System.Globalization;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Retention;

/// <summary>
/// Files <see cref="DurableFile"/> moved aside as unreadable (<c>&lt;file&gt;.corrupt-&lt;ticks&gt;</c>).
/// Each read failure made one and nothing removed them (#492). The newest
/// <c>Retention:CorruptKeepCount</c> per file stay for a diagnosis, each at most
/// <c>CorruptKeepDays</c>.
/// </summary>
public static class CorruptFiles
{
    public const string PrunerName = "corrupt-files";

    public static IPruner Pruner(AppSettings settings) =>
        new Pruner(
            PrunerName,
            "Files moved aside as unreadable (*.corrupt-*) beyond the newest Retention:CorruptKeepCount per file, or older than CorruptKeepDays.",
            now =>
            {
                RetentionSettings retention = settings?.Retention ?? new RetentionSettings();
                return Plan(
                    settings?.Location?.DataDirectory,
                    retention.EffectiveCorruptKeepCount,
                    retention.EffectiveCorruptKeepDays,
                    now
                );
            }
        );

    /// <summary>What the pruner removes now, anywhere under <paramref name="dataDirectory"/>. Writes nothing.</summary>
    public static PrunePlan Plan(
        string dataDirectory,
        int keepCount,
        int keepDays,
        DateTimeOffset utcNow
    )
    {
        var plan = new PrunePlan(PrunerName);
        if (string.IsNullOrWhiteSpace(dataDirectory) || !Directory.Exists(dataDirectory))
        {
            return plan;
        }

        DateTime before = utcNow.AddDays(-Math.Max(1, keepDays)).UtcDateTime;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        var groups = new DirectoryInfo(dataDirectory)
            .EnumerateFiles("*" + DurableFile.CorruptSuffix + "*", options)
            .Select(file => (File: file, Original: Original(file.FullName)))
            .Where(item => item.Original != null)
            .GroupBy(item => item.Original, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            int kept = 0;
            foreach (
                FileInfo file in group
                    .Select(item => item.File)
                    .OrderByDescending(file => file.LastWriteTimeUtc)
            )
            {
                if (kept < Math.Max(0, keepCount) && file.LastWriteTimeUtc >= before)
                {
                    kept++;
                    continue;
                }

                plan.Add(
                    PruneItem.ForFile(
                        file,
                        file.LastWriteTimeUtc < before
                            ? $"unreadable copy of {Path.GetFileName(group.Key)} older than Retention:CorruptKeepDays ({keepDays})"
                            : $"unreadable copy of {Path.GetFileName(group.Key)} beyond the newest Retention:CorruptKeepCount ({keepCount})"
                    )
                );
            }
        }

        return plan;
    }

    /// <summary>The file a corrupt copy was moved from, or null when the name is not one.</summary>
    private static string Original(string path)
    {
        int at = path.LastIndexOf(DurableFile.CorruptSuffix, StringComparison.OrdinalIgnoreCase);
        if (at <= 0)
        {
            return null;
        }

        string ticks = path.Substring(at + DurableFile.CorruptSuffix.Length);
        return long.TryParse(ticks, NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? path.Substring(0, at)
            : null;
    }
}
