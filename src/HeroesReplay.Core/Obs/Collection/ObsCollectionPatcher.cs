using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HeroesReplay.Core.Obs.Collection;

/// <summary>What one write of the live scene collection is asked to do.</summary>
public sealed record ObsCollectionUpdate
{
    /// <summary>The install's <c>obs\Default.json</c>.</summary>
    public string TemplatePath { get; init; }

    /// <summary>The live collection, <c>%APPDATA%\obs-studio\basic\scenes\&lt;name&gt;.json</c>.</summary>
    public string DestinationPath { get; init; }

    /// <summary>The effective <c>Location:DataDirectory</c>, for the report pages.</summary>
    public string DataDirectory { get; init; }

    public bool ObsIsRunning { get; init; }

    /// <summary>
    /// Name written into a collection installed from the template. OBS lists a collection by that
    /// name. A live collection whose paths are only updated keeps its name.
    /// </summary>
    public string CollectionName { get; init; }

    public ObsManagedFiles Managed { get; init; }

    /// <summary>
    /// A release is being installed. With no record of the template the live collection came from,
    /// a collection with the template's names (or the previous template's) is replaced, as every
    /// release did before that record existed. Otherwise only its paths are updated.
    /// </summary>
    public bool Release { get; init; }

    /// <summary>The <c>obs\Default.json</c> of the install being replaced, when there is one.</summary>
    public string PreviousTemplatePath { get; init; }

    /// <summary>
    /// Values the spectator sets per replay (<see cref="ObsRuntimeValues.From"/>). A merge does
    /// not count them as the operator's. Null: every difference counts, so a merge is refused
    /// more often, never less.
    /// </summary>
    public ObsRuntimeValues Runtime { get; init; }

    public DateTime UtcNow { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Keeps the live scene collection in step with the install's <c>obs\Default.json</c>, without
/// losing an operator's work. A collection whose scenes and sources are the ones HeroesReplay last
/// wrote (<see cref="ObsManagedFiles"/>), or the template's, is managed: it is replaced when the
/// template changed and otherwise only has its asset and data paths pointed at this install. Any
/// other collection is custom. When the template changed and the operator only added to a custom
/// collection (scenes, sources, filters, settings), the template's changes are merged in and the
/// additions kept (<see cref="ObsCollectionMerge"/>, #307); otherwise it is never overwritten.
/// Every write is an <see cref="ObsFileTransaction"/>, and nothing is written while OBS is
/// running: the change waits until the next <c>services start</c>, <c>update install-obs</c>, or
/// OBS launch by HeroesReplay that finds OBS closed (a replacement can go in through the live swap).
/// </summary>
public static class ObsCollectionPatcher
{
    /// <summary>The record's template hash when the template a live collection came from is not known.</summary>
    public const string UnknownTemplate = "unknown";

    public static ObsCollectionApplyResult ApplyForInstall(
        string startDirectory,
        string dataDirectory,
        bool obsIsRunning,
        string collectionName,
        ObsManagedFiles managed,
        ObsRuntimeValues runtime = null
    )
    {
        string template = ObsCollectionPaths.FindCollection(startDirectory);
        if (template == null)
        {
            return ObsCollectionApplyResult.Drifted(
                "OBS collection was not found next to the install."
            );
        }

        return Apply(
            new ObsCollectionUpdate
            {
                TemplatePath = template,
                DestinationPath = LiveCollectionPath(null, collectionName),
                DataDirectory = dataDirectory,
                ObsIsRunning = obsIsRunning,
                CollectionName = collectionName,
                Managed = managed,
                Runtime = runtime,
            }
        );
    }

    public static string LiveCollectionPath(string appData, string collectionName = null) =>
        ObsNames.CollectionFile(appData, collectionName);

    public static ObsCollectionApplyResult Apply(ObsCollectionUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(update.Managed);
        if (string.IsNullOrWhiteSpace(update.TemplatePath) || !File.Exists(update.TemplatePath))
        {
            return ObsCollectionApplyResult.Drifted("OBS collection template was not found.");
        }

        if (string.IsNullOrWhiteSpace(update.DestinationPath))
        {
            return ObsCollectionApplyResult.Drifted("OBS collection destination was not set.");
        }

        string templateFull = Path.GetFullPath(update.TemplatePath);
        string destinationFull = Path.GetFullPath(update.DestinationPath);
        if (string.Equals(templateFull, destinationFull, StringComparison.OrdinalIgnoreCase))
        {
            return ObsCollectionApplyResult.Drifted(
                "Refusing to rewrite the packaged collection in place."
            );
        }

        string template = File.ReadAllText(templateFull);
        IReadOnlyList<string> templateNames = Names(template);
        if (templateNames == null)
        {
            return ObsCollectionApplyResult.Drifted(
                "The OBS collection template is not valid JSON."
            );
        }

        var target = new Target(update, templateFull, destinationFull, template, templateNames);

        // Every template this install writes from, and the one it replaces, is kept as the base
        // of a later merge (#307), so the base outlives app.previous.
        update.Managed.SaveTemplate(template, update.UtcNow);
        string previousTemplate = ReadText(update.PreviousTemplatePath);
        if (previousTemplate != null)
        {
            update.Managed.SaveTemplate(previousTemplate, update.UtcNow);
        }

        // A release rollback that waited for OBS comes first, when this is the install it restored.
        ObsCollectionApplyResult restored = ObsCollectionRollback.CompletePending(
            update.Managed,
            destinationFull,
            target.Hash,
            templateNames,
            update.ObsIsRunning,
            update.UtcNow
        );
        if (restored != null)
        {
            return restored;
        }

        if (!File.Exists(destinationFull))
        {
            return update.ObsIsRunning
                ? ObsCollectionApplyResult.Defer(
                    "OBS is running, so the live collection was not created. It is created the next time HeroesReplay finds OBS closed."
                )
                : Install(target, "Installed the OBS collection for this install.");
        }

        string live;
        try
        {
            live = File.ReadAllText(destinationFull);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ObsCollectionApplyResult.Drifted(
                "The live OBS collection could not be read. It was not overwritten. " + e.Message
            );
        }

        IReadOnlyList<string> liveNames = Names(live);
        if (liveNames == null)
        {
            return ObsCollectionApplyResult.Drifted(
                "The live OBS collection is not valid JSON. It was not overwritten."
            );
        }

        ObsManagedCollection record = update.Managed.Read(destinationFull);
        IReadOnlyList<string> previousNames = Names(previousTemplate);

        // A collection a merge wrote keeps the operator's work: its record names its own scenes
        // and sources, and it is never replaced with the template.
        bool merged = record?.Merged == true;
        bool managed = merged
            ? !Drift(record.Sources, liveNames).Custom
            : new[] { record?.Sources, previousNames, templateNames }.Any(names =>
                names != null && !Drift(names, liveNames).Custom
            );
        bool templateChanged =
            record != null
            && !string.Equals(record.TemplateSha256, UnknownTemplate, StringComparison.Ordinal)
            && !string.Equals(record.TemplateSha256, target.Hash, StringComparison.Ordinal);
        if (!managed || merged && templateChanged)
        {
            string custom = managed
                ? "The live OBS collection keeps the operator's work from an earlier merge."
                : DescribeCustom(record?.Sources ?? templateNames, liveNames);
            return templateChanged
                ? MergeInto(target, record, live, previousTemplate, custom)
                : ObsCollectionApplyResult.Drifted(custom);
        }

        bool unrecorded = record == null;
        if (unrecorded && update.Release)
        {
            // A release with no record. Nothing says which template wrote the live collection:
            // the same scene and source names do not mean the same filters and settings (an
            // older build's match report Scroll filter, #197). So it is replaced once. Saved
            // now, so a replacement deferred while OBS runs still happens once it is closed.
            record = new ObsManagedCollection(
                UnknownTemplate,
                liveNames.Order(StringComparer.Ordinal).ToList(),
                update.UtcNow
            );
            update.Managed.Save(destinationFull, record);
        }

        // Not a release and no record (services start, the spectator): only the paths are
        // updated, and no record is saved, so the next release still replaces it once (#218).
        if (
            record != null
            && !string.Equals(record.TemplateSha256, target.Hash, StringComparison.Ordinal)
        )
        {
            return update.ObsIsRunning
                ? ObsCollectionApplyResult.Defer(
                    "This install's OBS collection template is newer than the live collection. OBS is running, so the collection is replaced the next time HeroesReplay finds OBS closed.",
                    new ObsCollectionReplacement(
                        target.DestinationPath,
                        Installed(target),
                        target.Hash,
                        target.TemplateNames.Order(StringComparer.Ordinal).ToList()
                    )
                )
                : Install(target, "Replaced the live OBS collection with this install's template.");
        }

        string updated = ObsCollectionPaths.Rewrite(live, target.AssetRoot, update.DataDirectory);
        if (string.Equals(updated, live, StringComparison.Ordinal))
        {
            return ObsCollectionApplyResult.Unchanged(
                update.ObsIsRunning
                    ? "OBS is running. Collection paths already match this install."
                    : "OBS collection paths already match this install."
            );
        }

        if (update.ObsIsRunning)
        {
            return ObsCollectionApplyResult.Defer(
                "OBS is running. The collection paths are updated the next time HeroesReplay finds OBS closed."
            );
        }

        string backup = ObsFileTransaction.Write(
            destinationFull,
            updated,
            update.Managed.BackupDirectory,
            update.UtcNow
        );
        if (!unrecorded)
        {
            Remember(target, liveNames, merged);
        }

        return ObsCollectionApplyResult.Updated(
            "Updated OBS collection paths for this install." + Saved(backup),
            backup
        );
    }

    /// <summary>
    /// Source names (scenes included) in the live collection and not in the package, and the
    /// reverse. Any difference makes the live collection custom: it is not rewritten.
    /// </summary>
    public static ObsNameDrift Drift(IEnumerable<string> packaged, IEnumerable<string> live)
    {
        var packagedSet = new HashSet<string>(packaged ?? [], StringComparer.Ordinal);
        var liveSet = new HashSet<string>(live ?? [], StringComparer.Ordinal);
        return new ObsNameDrift(
            liveSet.Where(name => !packagedSet.Contains(name)).ToList(),
            packagedSet.Where(name => !liveSet.Contains(name)).ToList()
        );
    }

    private sealed record Target(
        ObsCollectionUpdate Update,
        string TemplatePath,
        string DestinationPath,
        string Template,
        IReadOnlyList<string> TemplateNames
    )
    {
        public string AssetRoot => Path.GetDirectoryName(TemplatePath);

        public string Hash { get; } = Sha256(Template);
    }

    /// <summary>
    /// The hash a template is recorded under in <see cref="ObsManagedFiles"/>, or null when the
    /// file is missing or cannot be read.
    /// </summary>
    public static string TemplateHash(string templatePath)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(templatePath) && File.Exists(templatePath)
                ? Sha256(File.ReadAllText(templatePath))
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The hash a template's text is recorded and stored under (<see cref="ObsManagedFiles.SaveTemplate"/>).</summary>
    public static string HashOf(string template) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(template ?? string.Empty)));

    private static string Sha256(string template) => HashOf(template);

    private static ObsCollectionApplyResult Install(Target target, string message)
    {
        ObsCollectionUpdate update = target.Update;
        string backup = ObsFileTransaction.Write(
            target.DestinationPath,
            Installed(target),
            update.Managed.BackupDirectory,
            update.UtcNow
        );
        Remember(target, target.TemplateNames);
        return ObsCollectionApplyResult.Installed(message + Saved(backup), backup);
    }

    /// <summary>The template as this install writes it: its paths, under the collection's name.</summary>
    private static string Installed(Target target)
    {
        ObsCollectionUpdate update = target.Update;
        string installed = ObsCollectionPaths.Rewrite(
            target.Template,
            target.AssetRoot,
            update.DataDirectory
        );
        return string.IsNullOrWhiteSpace(update.CollectionName)
            ? installed
            : ObsNames.WithCollectionName(installed, update.CollectionName);
    }

    /// <summary>
    /// Writes <paramref name="replacement"/> over the live collection file and records its
    /// template. Only for a collection OBS does not have active (<see cref="ObsLiveCollectionSwap"/>).
    /// </summary>
    public static string WriteReplacement(
        ObsCollectionReplacement replacement,
        ObsManagedFiles managed,
        DateTime utcNow
    )
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(managed);
        string backup = ObsFileTransaction.Write(
            replacement.DestinationPath,
            replacement.Contents,
            managed.BackupDirectory,
            utcNow
        );
        managed.Save(
            replacement.DestinationPath,
            new ObsManagedCollection(replacement.TemplateSha256, replacement.Names, utcNow)
        );

        // The latest write wins: a rollback that waited for this file is done or superseded.
        managed.ClearPendingRestore(replacement.DestinationPath);
        return backup;
    }

    private static void Remember(Target target, IReadOnlyList<string> names, bool merged = false) =>
        target.Update.Managed.Save(
            target.DestinationPath,
            new ObsManagedCollection(
                target.Hash,
                names.Order(StringComparer.Ordinal).ToList(),
                target.Update.UtcNow,
                merged
            )
        );

    /// <summary>
    /// The template changed and the live collection is custom, or one a merge wrote: the
    /// template's changes are merged in when the operator only added to it (scenes, sources,
    /// filters, settings) and nothing conflicts. Anything else keeps the collection as it is, as
    /// every custom collection was kept before (#307). While OBS runs the merge waits until
    /// HeroesReplay finds OBS closed: it is never swapped in live, because OBS could save newer
    /// operator changes over a merge made from the file.
    /// </summary>
    private static ObsCollectionApplyResult MergeInto(
        Target target,
        ObsManagedCollection record,
        string live,
        string previousTemplate,
        string custom
    )
    {
        ObsCollectionUpdate update = target.Update;
        ObsCollectionBase found = ObsCollectionMerge.FindBase(
            record,
            target.Template,
            target.Hash,
            previousTemplate,
            update.Managed
        );
        if (found.Text == null)
        {
            return ObsCollectionApplyResult.Drifted(
                custom
                    + " The template it was last written from ("
                    + Short(record.TemplateSha256)
                    + ") is not stored here, so this install's changes cannot be merged into it."
            );
        }

        ObsMergeResult merge;
        try
        {
            merge = ObsCollectionMerge.Merge(
                ObsCollectionMerge.Normalize(found.Text, target.AssetRoot, update.DataDirectory),
                ObsCollectionMerge.Normalize(
                    target.Template,
                    target.AssetRoot,
                    update.DataDirectory
                ),
                ObsCollectionMerge.Normalize(live, target.AssetRoot, update.DataDirectory),
                update.Runtime,
                ObsMergeScope.AdditionsOnly
            );
        }
        catch (JsonException e)
        {
            return ObsCollectionApplyResult.Drifted(
                custom + " It could not be compared with the template: " + e.Message
            );
        }

        if (!merge.Ok)
        {
            return ObsCollectionApplyResult.Drifted(custom + " " + merge.Message);
        }

        if (update.ObsIsRunning)
        {
            return ObsCollectionApplyResult.Defer(
                "OBS is running. This install's template changes ("
                    + merge.Taken
                    + ") are merged into the live collection, keeping the operator's additions ("
                    + merge.Kept
                    + "), the next time HeroesReplay finds OBS closed.",
                merged: true
            );
        }

        IReadOnlyList<string> names = Names(merge.Merged) ?? target.TemplateNames;
        bool write = !string.Equals(merge.Merged, live, StringComparison.Ordinal);
        string backup = write
            ? ObsFileTransaction.Write(
                target.DestinationPath,
                merge.Merged,
                update.Managed.BackupDirectory,
                update.UtcNow
            )
            : null;
        Remember(target, names, merge.Kept > 0);
        return ObsCollectionApplyResult.Merging(
            (
                write
                    ? "Merged this install's OBS collection template changes ("
                        + merge.Taken
                        + ") into the live collection and kept the operator's additions ("
                        + merge.Kept
                        + ")."
                    : "The live OBS collection already has this install's template changes; its record now names this template."
            ) + Saved(backup),
            write,
            backup
        );
    }

    private static string Short(string hash) =>
        string.IsNullOrEmpty(hash) ? "none" : hash.Substring(0, Math.Min(12, hash.Length));

    private static string Saved(string backup) =>
        backup == null ? string.Empty : " The previous collection was saved to " + backup + ".";

    private static string ReadText(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
                ? File.ReadAllText(path)
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> Names(string json)
    {
        if (json == null)
        {
            return null;
        }

        try
        {
            return ObsCollectionPaths.SourceNames(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string DescribeCustom(IReadOnlyList<string> managed, IReadOnlyList<string> live)
    {
        ObsNameDrift drift = Drift(managed, live);
        return "The live OBS collection does not have the scenes and sources HeroesReplay manages (extra: "
            + Join(drift.Extra)
            + "; missing: "
            + Join(drift.Missing)
            + "). It was not overwritten. When the template changes and the operator only added to it, an update merges the template's changes in; otherwise remove the extra scenes and sources in OBS to let releases replace it, or close OBS and run heroesreplay obs apply --backup.";
    }

    private static string Join(IReadOnlyList<string> names) =>
        names.Count == 0 ? "none" : string.Join(", ", names.Take(8));
}

public sealed record ObsNameDrift(IReadOnlyList<string> Extra, IReadOnlyList<string> Missing)
{
    public bool Custom => Extra.Count > 0 || Missing.Count > 0;
}

/// <param name="Wrote">The live collection was written.</param>
/// <param name="Drift">The live collection is custom or unreadable and was left as it is.</param>
/// <param name="Message">One line for the log.</param>
/// <param name="Deferred">A write is due but OBS is running; it happens when OBS is closed.</param>
/// <param name="Backup">The copy of the collection taken before the write, when there was one.</param>
/// <param name="Replacement">
/// When the write is deferred because OBS runs and the template changed: the collection this
/// install would write, for <see cref="ObsLiveCollectionSwap"/>.
/// </param>
/// <param name="Merged">
/// The template's changes were merged into the live collection, or wait for OBS to close to be
/// merged, keeping the operator's additions (#307), instead of a replacement.
/// </param>
public sealed record ObsCollectionApplyResult(
    bool Wrote,
    bool Drift,
    string Message,
    bool Deferred = false,
    string Backup = null,
    ObsCollectionReplacement Replacement = null,
    bool Merged = false
)
{
    public static ObsCollectionApplyResult Installed(string message, string backup = null) =>
        new(true, false, message, Backup: backup);

    public static ObsCollectionApplyResult Updated(string message, string backup = null) =>
        new(true, false, message, Backup: backup);

    public static ObsCollectionApplyResult Merging(
        string message,
        bool wrote,
        string backup = null
    ) => new(wrote, false, message, Backup: backup, Merged: true);

    public static ObsCollectionApplyResult Unchanged(string message) => new(false, false, message);

    public static ObsCollectionApplyResult Drifted(string message) => new(false, true, message);

    public static ObsCollectionApplyResult Defer(
        string message,
        ObsCollectionReplacement replacement = null,
        bool merged = false
    ) => new(false, false, message, Deferred: true, Replacement: replacement, Merged: merged);
}

/// <summary>A managed live collection's new contents, written while OBS has it inactive.</summary>
/// <param name="DestinationPath">The live collection file.</param>
/// <param name="Contents">The template with this install's paths and the collection's name.</param>
/// <param name="TemplateSha256">The template's hash, recorded once the file is written.</param>
/// <param name="Names">The template's scene and source names, recorded with it.</param>
public sealed record ObsCollectionReplacement(
    string DestinationPath,
    string Contents,
    string TemplateSha256,
    IReadOnlyList<string> Names
);
