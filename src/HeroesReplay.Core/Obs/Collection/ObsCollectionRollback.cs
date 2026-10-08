using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HeroesReplay.Core.Obs.Collection;

/// <summary>
/// What a release install recorded about the live collection (<see cref="ObsManagedFiles.RollbackFileName"/>),
/// so a rollback can put back the collection the replaced build ran with.
/// </summary>
public sealed record ObsReleaseRollback
{
    /// <summary>The live collection file.</summary>
    public string CollectionPath { get; init; }

    /// <summary>
    /// When the install started. Every write of the collection from then on, by the install, a
    /// live swap, or <c>services start</c>, left a backup, and the first of them holds the
    /// collection the replaced build ran with.
    /// </summary>
    public DateTime InstalledAtUtc { get; init; }

    /// <summary>The backup the install's own write took, or null when it did not write then.</summary>
    public string Backup { get; init; }

    /// <summary>SHA-256 of the replaced install's <c>obs\Default.json</c>, the install a rollback restores.</summary>
    public string PreviousTemplateSha256 { get; init; }

    /// <summary>The collection's managed record before the install, or null when it had none.</summary>
    public ObsManagedCollection PreviousRecord { get; init; }

    /// <summary>The release that was installed, for the log.</summary>
    public string Version { get; init; }
}

/// <summary>
/// A rollback that could not put the collection back yet (<see cref="ObsManagedFiles.PendingRestoreFileName"/>):
/// OBS was running and the live swap could not run. The restored install finishes it the next
/// time it writes the collection: when it finds OBS closed, or at the next replay through the
/// live swap.
/// </summary>
public sealed record ObsPendingRestore
{
    public string CollectionPath { get; init; }

    /// <summary>The collection the restored build ran with.</summary>
    public string Backup { get; init; }

    /// <summary>
    /// The restored install's template hash. Only an install with that template finishes the
    /// restore; any other install drops it.
    /// </summary>
    public string TemplateSha256 { get; init; }

    /// <summary>The managed record saved once the backup is back.</summary>
    public ObsManagedCollection Record { get; init; }

    public DateTime DeferredAtUtc { get; init; }

    /// <summary>Why it could not run then.</summary>
    public string Reason { get; init; }
}

public enum ObsRollbackOutcome
{
    /// <summary>No release install recorded the collection.</summary>
    NoRecord,

    /// <summary>The record is for another pair of installs.</summary>
    OtherInstall,

    /// <summary>The release never wrote the collection: nothing to put back.</summary>
    NotWritten,

    /// <summary>OBS was closed, and the backup was written back.</summary>
    Restored,

    /// <summary>OBS was running, and the backup went in through the live swap.</summary>
    RestoredLive,

    /// <summary>The live collection already was the backup.</summary>
    AlreadyRestored,

    /// <summary>OBS was running and the swap could not run. <see cref="ObsPendingRestore"/> was saved.</summary>
    Deferred,

    /// <summary>The live collection was changed by hand after the release wrote it. It was kept.</summary>
    Custom,

    /// <summary>The file was put back, but OBS stayed on the spare collection.</summary>
    Stranded,

    /// <summary>The backup could not be read or the write failed. The collection was kept.</summary>
    Failed,
}

/// <param name="Outcome">What the rollback did with the live collection.</param>
/// <param name="Message">One line for the update log.</param>
/// <param name="Backup">The backup put back, when there was one.</param>
public sealed record ObsRollbackResult(
    ObsRollbackOutcome Outcome,
    string Message,
    string Backup = null
)
{
    /// <summary>False when the collection is not the one the restored build ran with and nothing will put it back.</summary>
    public bool Ok =>
        Outcome
            is not (
                ObsRollbackOutcome.OtherInstall
                or ObsRollbackOutcome.Custom
                or ObsRollbackOutcome.Stranded
                or ObsRollbackOutcome.Failed
            );
}

/// <summary>What <see cref="ObsCollectionRollback.Restore"/> needs.</summary>
public sealed record ObsRollbackRequest
{
    public ObsManagedFiles Managed { get; init; }

    /// <summary>The <c>obs\Default.json</c> of the install being restored (<c>app.previous</c>).</summary>
    public string PreviousTemplatePath { get; init; }

    /// <summary>
    /// The <c>obs\Default.json</c> of the failed install. A live collection with its scenes and
    /// sources is one the release wrote, not one the operator changed.
    /// </summary>
    public string FailedTemplatePath { get; init; }

    public bool ObsIsRunning { get; init; }

    /// <summary>
    /// Opens a websocket session for the live swap while OBS runs. Null when
    /// <c>OBS:LiveCollectionSwap</c> is off. A session that is <see cref="IDisposable"/> is
    /// disposed after the swap.
    /// </summary>
    public Func<IObsCollectionSwitch> OpenSwitch { get; init; }

    public DateTime UtcNow { get; init; } = DateTime.UtcNow;

    /// <summary>The pause between switch-back attempts, for tests.</summary>
    public Action<int> Wait { get; init; }
}

/// <summary>
/// Release rollback for the live scene collection (#304). A release install records which
/// collection the replaced build ran with (<see cref="Record"/>): the backup
/// <see cref="ObsFileTransaction"/> takes before the release's first write. On a rollback,
/// <see cref="Restore"/> puts exactly that file back under the same rules as every other write:
/// never over a custom collection, never into a running OBS's active file (a live swap through
/// the spare collection instead, which keeps the stream and a recording up), and the managed
/// record goes back to the restored template. When the release never wrote the collection,
/// nothing is restored.
/// </summary>
public static class ObsCollectionRollback
{
    /// <summary>
    /// Saves what a rollback of this install needs and returns one line for the update log.
    /// <paramref name="previousRecord"/> is the collection's record read before the install.
    /// </summary>
    public static string Record(
        ObsManagedFiles managed,
        string collectionPath,
        string previousTemplatePath,
        ObsManagedCollection previousRecord,
        ObsCollectionApplyResult applied,
        DateTime installedAtUtc,
        string version = null
    )
    {
        ArgumentNullException.ThrowIfNull(managed);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionPath);
        var rollback = new ObsReleaseRollback
        {
            CollectionPath = Path.GetFullPath(collectionPath),
            InstalledAtUtc = installedAtUtc.ToUniversalTime(),
            Backup = applied?.Backup,
            PreviousTemplateSha256 = ObsCollectionPatcher.TemplateHash(previousTemplatePath),
            PreviousRecord = previousRecord,
            Version = string.IsNullOrWhiteSpace(version) ? null : version,
        };
        managed.SaveRollback(rollback);
        if (rollback.Backup != null)
        {
            return "A rollback of this release puts back " + rollback.Backup + ".";
        }

        return applied?.Wrote == true
            ? "This release created the collection, so a rollback has no earlier one to put back."
            : "This release has not written the collection. A rollback puts back the collection as it was before this release's first write, if it makes one.";
    }

    /// <summary>
    /// Puts back the collection the restored build ran with. Run by the failed build, while it
    /// is still installed, before the previous install is copied back (<c>apply-release.ps1</c>).
    /// </summary>
    public static ObsRollbackResult Restore(ObsRollbackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Managed);
        ObsManagedFiles managed = request.Managed;
        ObsReleaseRollback rollback = managed.ReadRollback();
        if (rollback == null)
        {
            return new ObsRollbackResult(
                ObsRollbackOutcome.NoRecord,
                "No release recorded the OBS collection ("
                    + managed.RollbackPath
                    + " is missing), so it was left as it is."
            );
        }

        string previousHash = ObsCollectionPatcher.TemplateHash(request.PreviousTemplatePath);
        if (
            previousHash != null
            && rollback.PreviousTemplateSha256 != null
            && !string.Equals(
                previousHash,
                rollback.PreviousTemplateSha256,
                StringComparison.Ordinal
            )
        )
        {
            return new ObsRollbackResult(
                ObsRollbackOutcome.OtherInstall,
                managed.RollbackPath
                    + " was written for another install: its replaced template is "
                    + Short(rollback.PreviousTemplateSha256)
                    + ", the install being restored has "
                    + Short(previousHash)
                    + ". The OBS collection was left as it is."
            );
        }

        string destination = rollback.CollectionPath;
        string source = SourceBackup(rollback, managed);
        if (source == null)
        {
            // The release saved at most a record (a first release with no record saves one before
            // a deferred replacement). The record goes back to what it was.
            managed.Put(destination, rollback.PreviousRecord);
            managed.ClearRollback();
            return new ObsRollbackResult(
                ObsRollbackOutcome.NotWritten,
                "The failed release did not write the OBS collection (no backup of "
                    + destination
                    + " since "
                    + rollback.InstalledAtUtc.ToString("u")
                    + "), so there is nothing to put back."
            );
        }

        byte[] contents;
        try
        {
            contents = File.ReadAllBytes(source);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ObsRollbackResult(
                ObsRollbackOutcome.Failed,
                "The backup "
                    + source
                    + " could not be read, so the collection was kept. "
                    + e.Message,
                source
            );
        }

        IReadOnlyList<string> backupNames = Names(Decode(contents));
        if (backupNames == null)
        {
            return new ObsRollbackResult(
                ObsRollbackOutcome.Failed,
                "The backup " + source + " is not valid JSON, so the collection was kept.",
                source
            );
        }

        string restoredTemplate = rollback.PreviousTemplateSha256 ?? previousHash;
        ObsManagedCollection record = RecordAfter(
            rollback.PreviousRecord,
            restoredTemplate,
            backupNames,
            request.UtcNow
        );
        ObsCollectionApplyResult file;
        try
        {
            file = RestoreFile(
                destination,
                contents,
                record,
                managed,
                request.ObsIsRunning,
                [managed.Read(destination)?.Sources, TemplateNames(request.FailedTemplatePath)],
                request.UtcNow
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ObsRollbackResult(
                ObsRollbackOutcome.Failed,
                "The OBS collection was not written back, and it is as it was. " + e.Message,
                source
            );
        }

        if (file.Drift)
        {
            return new ObsRollbackResult(
                ObsRollbackOutcome.Custom,
                file.Message
                    + " The collection the restored build ran with is "
                    + source
                    + ": close OBS and copy it over "
                    + destination
                    + " to put it back by hand.",
                source
            );
        }

        if (!file.Deferred)
        {
            managed.ClearRollback();
            return new ObsRollbackResult(
                file.Wrote ? ObsRollbackOutcome.Restored : ObsRollbackOutcome.AlreadyRestored,
                file.Wrote
                    ? "Put back the OBS collection the restored build ran with, from "
                        + source
                        + "."
                        + Saved(file.Backup)
                    : file.Message,
                source
            );
        }

        return SwapOrDefer(request, rollback, source, file.Replacement, record, restoredTemplate);
    }

    /// <summary>
    /// The live collection's pending restore, when the install now running is the one it waits
    /// for. Null when there is none: the caller goes on as usual. Called by
    /// <see cref="ObsCollectionPatcher.Apply"/> before anything else, so <c>services start</c>,
    /// <c>update install-obs</c>, and the spectator's next replay all finish it.
    /// </summary>
    internal static ObsCollectionApplyResult CompletePending(
        ObsManagedFiles managed,
        string destination,
        string templateHash,
        IReadOnlyList<string> templateNames,
        bool obsIsRunning,
        DateTime utcNow
    )
    {
        ObsPendingRestore pending = managed.ReadPendingRestore();
        if (pending == null || !ObsManagedFiles.SamePath(pending.CollectionPath, destination))
        {
            return null;
        }

        if (!string.Equals(pending.TemplateSha256, templateHash, StringComparison.Ordinal))
        {
            // Another install runs now: the rollback it waited for no longer applies.
            managed.ClearPendingRestore(destination);
            return null;
        }

        byte[] contents;
        try
        {
            contents = File.ReadAllBytes(pending.Backup);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            managed.ClearPendingRestore(destination);
            return null;
        }

        IReadOnlyList<string> backupNames = Names(Decode(contents));
        if (backupNames == null)
        {
            managed.ClearPendingRestore(destination);
            return null;
        }

        ObsCollectionApplyResult result = RestoreFile(
            destination,
            contents,
            pending.Record ?? RecordAfter(null, templateHash, backupNames, utcNow),
            managed,
            obsIsRunning,
            [managed.Read(destination)?.Sources, templateNames],
            utcNow
        );
        if (!result.Deferred)
        {
            // Written, already in place, or custom (never overwritten): the wait is over.
            managed.ClearPendingRestore(destination);
        }

        return result with
        {
            Message = "Release rollback: " + result.Message,
        };
    }

    /// <summary>One line for <c>services status</c>, or null when no rollback waits.</summary>
    public static string DescribePending(ObsManagedFiles managed)
    {
        ObsPendingRestore pending = managed?.ReadPendingRestore();
        return pending == null
            ? null
            : "A release rollback waits to put back "
                + pending.Backup
                + " over "
                + pending.CollectionPath
                + " (since "
                + pending.DeferredAtUtc.ToString("u")
                + ": "
                + pending.Reason
                + "). The restored build does it the next time it finds OBS closed, or at its next replay through the live swap.";
    }

    /// <summary>
    /// Writes <paramref name="contents"/> over <paramref name="destination"/> and saves
    /// <paramref name="record"/>, or returns why not: the live collection is custom (it has
    /// none of the <paramref name="managedNames"/> sets, nor the backup's own names), or OBS is
    /// running (deferred with the replacement for a live swap).
    /// </summary>
    private static ObsCollectionApplyResult RestoreFile(
        string destination,
        byte[] contents,
        ObsManagedCollection record,
        ObsManagedFiles managed,
        bool obsIsRunning,
        IReadOnlyList<IReadOnlyList<string>> managedNames,
        DateTime utcNow
    )
    {
        string text = Decode(contents);
        IReadOnlyList<string> backupNames = Names(text) ?? [];
        if (File.Exists(destination))
        {
            byte[] current;
            try
            {
                current = File.ReadAllBytes(destination);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return ObsCollectionApplyResult.Drifted(
                    "The live OBS collection could not be read. It was not overwritten. "
                        + e.Message
                );
            }

            if (current.AsSpan().SequenceEqual(contents))
            {
                managed.Put(destination, record with { WrittenAtUtc = utcNow });
                return ObsCollectionApplyResult.Unchanged(
                    "The OBS collection already is the one the restored build ran with."
                );
            }

            IReadOnlyList<string> liveNames = Names(Decode(current));
            if (liveNames == null)
            {
                return ObsCollectionApplyResult.Drifted(
                    "The live OBS collection is not valid JSON. It was not overwritten."
                );
            }

            bool known = managedNames
                .Append(backupNames)
                .Any(names =>
                    names != null && !ObsCollectionPatcher.Drift(names, liveNames).Custom
                );
            if (!known)
            {
                ObsNameDrift drift = ObsCollectionPatcher.Drift(backupNames, liveNames);
                return ObsCollectionApplyResult.Drifted(
                    "The live OBS collection was changed after the release wrote it (extra: "
                        + Join(drift.Extra)
                        + "; missing: "
                        + Join(drift.Missing)
                        + "). It was not overwritten."
                );
            }
        }

        if (obsIsRunning)
        {
            return ObsCollectionApplyResult.Defer(
                "OBS is running, so the collection the restored build ran with goes back through the live swap, or the next time HeroesReplay finds OBS closed.",
                new ObsCollectionReplacement(
                    Path.GetFullPath(destination),
                    text,
                    record.TemplateSha256,
                    record.Sources
                )
            );
        }

        string backup = ObsFileTransaction.Write(
            destination,
            contents,
            managed.BackupDirectory,
            utcNow
        );
        managed.Put(destination, record with { WrittenAtUtc = utcNow });
        managed.ClearPendingRestore(destination);
        return ObsCollectionApplyResult.Installed(
            "Put back the OBS collection the restored build ran with." + Saved(backup),
            backup
        );
    }

    private static ObsRollbackResult SwapOrDefer(
        ObsRollbackRequest request,
        ObsReleaseRollback rollback,
        string source,
        ObsCollectionReplacement replacement,
        ObsManagedCollection record,
        string restoredTemplate
    )
    {
        ObsManagedFiles managed = request.Managed;
        string collection = Path.GetFileNameWithoutExtension(rollback.CollectionPath);
        string reason;
        if (request.OpenSwitch == null)
        {
            reason = "OBS:LiveCollectionSwap is off";
        }
        else
        {
            try
            {
                IObsCollectionSwitch obs = request.OpenSwitch();
                try
                {
                    ObsLiveSwapResult swap = ObsLiveCollectionSwap.Run(
                        obs,
                        replacement,
                        collection,
                        managed,
                        request.UtcNow,
                        request.Wait
                    );
                    if (swap.Swapped)
                    {
                        managed.ClearRollback();
                        return new ObsRollbackResult(
                            ObsRollbackOutcome.RestoredLive,
                            "Put back the OBS collection the restored build ran with, from "
                                + source
                                + ", while OBS runs (live swap through '"
                                + ObsLiveCollectionSwap.SpareName(collection)
                                + "'; the stream and a recording stay up). "
                                + swap.Message,
                            source
                        );
                    }

                    if (swap.Stranded && swap.Backup != null)
                    {
                        // The file is back and recorded; OBS is still on the spare, which has the
                        // same layout. The next replay switches it back (ObsLiveCollectionSwap.Recover).
                        managed.ClearRollback();
                        return new ObsRollbackResult(
                            ObsRollbackOutcome.Stranded,
                            "Put back the OBS collection file from "
                                + source
                                + ", but "
                                + swap.Message,
                            source
                        );
                    }

                    reason = swap.Message;
                }
                finally
                {
                    (obs as IDisposable)?.Dispose();
                }
            }
            catch (Exception e)
            {
                reason = "the live swap could not run: " + e.Message;
            }
        }

        managed.SavePendingRestore(
            new ObsPendingRestore
            {
                CollectionPath = rollback.CollectionPath,
                Backup = source,
                TemplateSha256 = restoredTemplate,
                Record = record,
                DeferredAtUtc = request.UtcNow.ToUniversalTime(),
                Reason = reason,
            }
        );
        managed.ClearRollback();
        return new ObsRollbackResult(
            ObsRollbackOutcome.Deferred,
            "OBS is running and the collection the restored build ran with was not swapped in ("
                + reason
                + "). The rollback waits in "
                + managed.PendingRestorePath
                + " (services status shows it): the restored build puts back "
                + source
                + " the next time it finds OBS closed, or at its next replay through the live swap. A build from before this rollback existed replaces the collection with its own template instead.",
            source
        );
    }

    /// <summary>
    /// The backup the install's own write took, or else the first backup of the collection from
    /// after the install started: the collection as the replaced build left it.
    /// </summary>
    private static string SourceBackup(ObsReleaseRollback rollback, ObsManagedFiles managed)
    {
        if (!string.IsNullOrWhiteSpace(rollback.Backup) && File.Exists(rollback.Backup))
        {
            return rollback.Backup;
        }

        return ObsFileTransaction
            .BackupsSince(managed.BackupDirectory, rollback.CollectionPath, rollback.InstalledAtUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// The record goes back to what it was before the install. With none then, it names the
    /// restored install's template, so the restored build keeps the file instead of replacing it.
    /// </summary>
    private static ObsManagedCollection RecordAfter(
        ObsManagedCollection previous,
        string templateHash,
        IReadOnlyList<string> names,
        DateTime utcNow
    ) =>
        previous
        ?? new ObsManagedCollection(
            templateHash ?? ObsCollectionPatcher.UnknownTemplate,
            names.Order(StringComparer.Ordinal).ToList(),
            utcNow
        );

    private static IReadOnlyList<string> TemplateNames(string templatePath)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(templatePath) && File.Exists(templatePath)
                ? Names(File.ReadAllText(templatePath))
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> Names(string json)
    {
        try
        {
            return ObsCollectionPaths.SourceNames(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>As <see cref="File.ReadAllText(string)"/> reads it: a UTF-8 BOM is dropped.</summary>
    private static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static string Saved(string backup) =>
        backup == null ? string.Empty : " The collection it replaced was saved to " + backup + ".";

    private static string Short(string hash) =>
        hash == null ? "unknown" : hash.Substring(0, Math.Min(12, hash.Length));

    private static string Join(IReadOnlyList<string> names) =>
        names.Count == 0 ? "none" : string.Join(", ", names.Take(8));
}
