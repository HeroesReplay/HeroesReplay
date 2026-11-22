using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.SelfUpdate;

/// <summary>
/// What the self-update cache must keep (#488): the installed version, the version
/// <c>app.previous</c> was mirrored from, the newest <see cref="ReleaseSettings.RetainedVersions"/>
/// older ones, every version newer than the install that is not on the skip list (staged or about
/// to be), and every folder a running process was started from.
/// </summary>
public sealed record ReleaseCacheFacts(
    string Installed,
    string Previous,
    ReleaseSkipList Skipped,
    IReadOnlyCollection<string> InUse,
    int RetainedVersions
);

/// <summary>
/// <c>%LOCALAPPDATA%\HeroesReplay\updates</c> and the release logs (#488). The release gate
/// downloads each release into <c>updates\&lt;tag&gt;</c> (the zip, <c>extract\</c> and
/// <c>prepared\</c>, about 160 MB), and nothing removed an old one, so the stream PC held 16
/// releases and 2.4 GB. <c>app.previous</c> is the rollback, so an installed release's folder only
/// keeps <c>prepared\</c>, where <c>apply-release.ps1</c> runs from.
/// </summary>
public static class ReleaseCache
{
    public const string PrunerName = "update-cache";
    public const string LogsPrunerName = "release-logs";
    public const string UpdatesFolderName = "updates";
    public const string MainLogName = "apply-release.log";

    /// <summary>A loose zip younger than this may be a download in progress, so it stays.</summary>
    public static readonly TimeSpan LooseZipGrace = TimeSpan.FromHours(1);

    public static IPruner Pruner(AppSettings settings, string stateDirectory) =>
        new Pruner(
            PrunerName,
            "Downloaded releases in updates\\: all but the installed one, app.previous's, the newest Release:RetainedVersions older ones, a staged newer one, and any a process runs from.",
            now =>
                Plan(
                    Path.Combine(stateDirectory, UpdatesFolderName),
                    FactsForThisInstall(settings, stateDirectory),
                    now
                )
        );

    public static IPruner LogsPruner(AppSettings settings, string stateDirectory) =>
        new Pruner(
            LogsPrunerName,
            "apply-release logs of earlier updates beyond the newest Release:RetainedLogs. apply-release.log, the last update's, stays.",
            _ =>
                PlanLogs(
                    string.IsNullOrWhiteSpace(settings?.ServiceLogs?.Directory)
                        ? Path.Combine(stateDirectory, "logs")
                        : settings.ServiceLogs.ResolvedDirectory,
                    Retained(settings?.Release?.RetainedLogs, ReleaseSettings.DefaultRetainedLogs)
                )
        );

    /// <summary>
    /// The facts for the install this process runs from: its <c>version.txt</c>, the one in
    /// <c>&lt;install&gt;.previous</c>, the skip list, and every process image under
    /// <c>updates</c>.
    /// </summary>
    public static ReleaseCacheFacts FactsForThisInstall(AppSettings settings, string stateDirectory)
    {
        string install = Path.GetDirectoryName(Environment.ProcessPath);
        string updates = Path.Combine(stateDirectory, UpdatesFolderName);
        return new ReleaseCacheFacts(
            ReleaseInstall.ReadVersion(install),
            string.IsNullOrWhiteSpace(install)
                ? string.Empty
                : ReleaseInstall.ReadVersion(
                    Path.TrimEndingDirectorySeparator(install) + ".previous"
                ),
            ReleaseSkipList.Load(Path.Combine(updates, ReleaseSkipList.FileName)),
            InUse(updates, ProcessTable.Snapshot().Select(process => process.ImagePath)),
            Retained(settings?.Release?.RetainedVersions, ReleaseSettings.DefaultRetainedVersions)
        );
    }

    /// <summary>The <c>updates</c> folder names that hold one of <paramref name="images"/>.</summary>
    public static IReadOnlyCollection<string> InUse(string updates, IEnumerable<string> images)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(updates))
        {
            return folders;
        }

        string root =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(updates))
            + Path.DirectorySeparatorChar;
        foreach (string image in images ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(image))
            {
                continue;
            }

            string full = Path.GetFullPath(image);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string first = full.Substring(root.Length).Split(Path.DirectorySeparatorChar)[0];
            if (first.Length > 0)
            {
                folders.Add(first);
            }
        }

        return folders;
    }

    /// <summary>What the update cache pruner removes now. Writes nothing.</summary>
    public static PrunePlan Plan(string updates, ReleaseCacheFacts facts, DateTimeOffset utcNow)
    {
        var plan = new PrunePlan(PrunerName);
        if (string.IsNullOrWhiteSpace(updates) || !Directory.Exists(updates) || facts == null)
        {
            return plan;
        }

        var root = new DirectoryInfo(updates);
        foreach (FileInfo zip in root.GetFiles("*.zip"))
        {
            if (zip.LastWriteTimeUtc < (utcNow - LooseZipGrace).UtcDateTime)
            {
                plan.Add(
                    PruneItem.ForFile(
                        zip,
                        "a downloaded release zip at the top of updates\\: no install reads it"
                    )
                );
            }
        }

        // A source build has no version.txt: without the installed version nothing is known to be old.
        if (!ReleaseVersion.TryParse(facts.Installed, out ReleaseVersion installed))
        {
            return plan;
        }

        var releases = new List<(DirectoryInfo Folder, ReleaseVersion Version)>();
        foreach (DirectoryInfo folder in root.GetDirectories())
        {
            if (ReleaseVersion.TryParse(folder.Name, out ReleaseVersion version))
            {
                releases.Add((folder, version));
            }
        }

        HashSet<string> kept = releases
            .Where(release => release.Version.CompareTo(installed) < 0)
            .OrderByDescending(release => release.Version)
            .Take(facts.RetainedVersions)
            .Select(release => release.Folder.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach ((DirectoryInfo folder, ReleaseVersion version) in releases)
        {
            string name = folder.Name;
            bool inUse = facts.InUse?.Contains(name) == true;
            int order = version.CompareTo(installed);
            if (inUse)
            {
                continue;
            }

            if (order > 0)
            {
                // Staged or about to be: the gate stages only a tag the skip list does not name.
                if (facts.Skipped?.Contains(name) == true)
                {
                    plan.Add(
                        PruneItem.ForDirectory(
                            folder,
                            $"release {name} failed its health gate here and is on skipped-releases.txt, so it is never installed"
                        )
                    );
                }

                continue;
            }

            bool installedOrRollback =
                order == 0
                || string.Equals(name, facts.Previous, StringComparison.OrdinalIgnoreCase);
            if (installedOrRollback || kept.Contains(name))
            {
                PlanSpentDownload(folder, name, plan);
                continue;
            }

            plan.Add(
                PruneItem.ForDirectory(
                    folder,
                    $"release {name}: older than the installed {facts.Installed}, not app.previous's ({(string.IsNullOrWhiteSpace(facts.Previous) ? "none" : facts.Previous)}), and beyond Release:RetainedVersions ({facts.RetainedVersions})"
                )
            );
        }

        return plan;
    }

    /// <summary>
    /// A kept release that is installed or older: its zip and <c>extract\</c> were only steps to
    /// <c>prepared\</c>, which <c>apply-release.ps1</c> runs from.
    /// </summary>
    private static void PlanSpentDownload(DirectoryInfo folder, string name, PrunePlan plan)
    {
        foreach (FileInfo zip in folder.GetFiles("*.zip"))
        {
            plan.Add(
                PruneItem.ForFile(zip, $"release {name} is unzipped: its zip is not read again")
            );
        }

        var extract = new DirectoryInfo(Path.Combine(folder.FullName, "extract"));
        if (extract.Exists)
        {
            plan.Add(
                PruneItem.ForDirectory(
                    extract,
                    $"release {name} is prepared: its extract\\ folder is not read again"
                )
            );
        }
    }

    /// <summary>
    /// The release logs beyond the newest <paramref name="retained"/>: each earlier update's
    /// <c>apply-release.&lt;time&gt;.log</c> and the <c>apply-release-&lt;tag&gt;.log</c> written
    /// when another window held the main log (#281). <c>apply-release.log</c> is never planned.
    /// </summary>
    public static PrunePlan PlanLogs(string logs, int retained)
    {
        var plan = new PrunePlan(LogsPrunerName);
        if (string.IsNullOrWhiteSpace(logs) || !Directory.Exists(logs))
        {
            return plan;
        }

        foreach (
            FileInfo log in new DirectoryInfo(logs)
                .GetFiles("apply-release*.log")
                .Where(log =>
                    !string.Equals(log.Name, MainLogName, StringComparison.OrdinalIgnoreCase)
                )
                .OrderByDescending(log => log.LastWriteTimeUtc)
                .Skip(retained)
        )
        {
            plan.Add(
                PruneItem.ForFile(
                    log,
                    $"an earlier update's log beyond the newest Release:RetainedLogs ({retained})"
                )
            );
        }

        return plan;
    }

    private static int Retained(int? value, int fallback) =>
        value is int count && count >= 0 ? count : fallback;
}

/// <summary>
/// A release tag: <c>v</c> and a Semantic Versioning 2.0.0 version, such as <c>v1.2.3</c> or
/// <c>v1.2.3-rc.1</c>, ordered by SemVer precedence (build metadata after <c>+</c> is ignored).
/// The tags before semantic versioning, <c>v1.0.0-614</c> to <c>v1.0.0-1137</c>, are 1.0.0
/// prereleases: they order by their number, and below every <c>v1.0.1</c> or later release, so
/// an install on one of them takes the first semantic release as newer. A name without the
/// <c>v</c>, or that is not SemVer, is not a release.
/// </summary>
public readonly record struct ReleaseVersion(long Major, long Minor, long Patch, string PreRelease)
    : IComparable<ReleaseVersion>
{
    public bool IsPreRelease => !string.IsNullOrEmpty(PreRelease);

    public static bool TryParse(string tag, out ReleaseVersion version)
    {
        version = default;
        string text = tag?.Trim();
        if (string.IsNullOrEmpty(text) || (text[0] != 'v' && text[0] != 'V'))
        {
            return false;
        }

        text = text.Substring(1);
        int plus = text.IndexOf('+');
        if (plus >= 0)
        {
            if (!ValidIdentifiers(text[(plus + 1)..], numericRule: false))
            {
                return false;
            }

            text = text[..plus];
        }

        string preRelease = string.Empty;
        int dash = text.IndexOf('-');
        if (dash >= 0)
        {
            preRelease = text[(dash + 1)..];
            if (!ValidIdentifiers(preRelease, numericRule: true))
            {
                return false;
            }

            text = text[..dash];
        }

        string[] core = text.Split('.');
        if (
            core.Length != 3
            || !TryNumber(core[0], out long major)
            || !TryNumber(core[1], out long minor)
            || !TryNumber(core[2], out long patch)
        )
        {
            return false;
        }

        version = new ReleaseVersion(major, minor, patch, preRelease);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        int order = Major.CompareTo(other.Major);
        if (order == 0)
        {
            order = Minor.CompareTo(other.Minor);
        }

        if (order == 0)
        {
            order = Patch.CompareTo(other.Patch);
        }

        if (order != 0)
        {
            return order;
        }

        // A release ranks above any prerelease of the same version.
        if (!IsPreRelease || !other.IsPreRelease)
        {
            return other.IsPreRelease.CompareTo(IsPreRelease);
        }

        string[] mine = PreRelease.Split('.');
        string[] theirs = other.PreRelease.Split('.');
        for (int i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            bool leftNumber = long.TryParse(mine[i], out long left);
            bool rightNumber = long.TryParse(theirs[i], out long right);
            int part = (leftNumber, rightNumber) switch
            {
                (true, true) => left.CompareTo(right),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(mine[i], theirs[i]),
            };
            if (part != 0)
            {
                return Math.Sign(part);
            }
        }

        return mine.Length.CompareTo(theirs.Length);
    }

    /// <summary>The tag, such as <c>v1.2.3</c> or <c>v1.2.3-rc.1</c>.</summary>
    public override string ToString() =>
        $"v{Major}.{Minor}.{Patch}" + (IsPreRelease ? "-" + PreRelease : string.Empty);

    private static bool TryNumber(string text, out long number)
    {
        number = 0;
        return text.Length > 0
            && (text.Length == 1 || text[0] != '0')
            && text.All(char.IsAsciiDigit)
            && long.TryParse(text, out number);
    }

    private static bool ValidIdentifiers(string text, bool numericRule)
    {
        if (text.Length == 0)
        {
            return false;
        }

        foreach (string identifier in text.Split('.'))
        {
            if (
                identifier.Length == 0
                || !identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            )
            {
                return false;
            }

            // SemVer: a numeric prerelease identifier has no leading zero.
            if (
                numericRule
                && identifier.Length > 1
                && identifier[0] == '0'
                && identifier.All(char.IsAsciiDigit)
            )
            {
                return false;
            }
        }

        return true;
    }
}
