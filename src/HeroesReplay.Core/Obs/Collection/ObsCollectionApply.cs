using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Obs.Collection;

/// <summary>What <see cref="ObsCollectionApply.Run"/> merges.</summary>
public sealed record ObsApplyRequest
{
    /// <summary>The <c>obs\Default.json</c> to merge in.</summary>
    public string TemplatePath { get; init; }

    /// <summary>The <c>obs\Default.json</c> of the install that wrote the live collection, when it is another one.</summary>
    public string PreviousTemplatePath { get; init; }

    /// <summary>The live collection, <c>%APPDATA%\obs-studio\basic\scenes\&lt;name&gt;.json</c>.</summary>
    public string CollectionPath { get; init; }

    /// <summary>The effective <c>Location:DataDirectory</c>, for the path rewrite.</summary>
    public string DataDirectory { get; init; }

    public ObsManagedFiles Managed { get; init; }

    public bool ObsIsRunning { get; init; }

    /// <summary><c>--backup</c>: back the live collection up and write the merge. Otherwise nothing is written.</summary>
    public bool Write { get; init; }

    /// <summary>Values the spectator sets per replay: neither compared nor merged.</summary>
    public ObsRuntimeValues Runtime { get; init; }

    /// <summary>
    /// <c>OBS:StableAssets</c>: the assets point at the verified copy an update uses
    /// (<see cref="ObsAssetStore"/>, #330), made before the merge is written.
    /// </summary>
    public bool StableAssets { get; init; }

    /// <summary>Where stable copies are kept; <see cref="ObsAssetStore.For"/> of <see cref="Managed"/> when null.</summary>
    public ObsAssetStore AssetStore { get; init; }

    public DateTime UtcNow { get; init; } = DateTime.UtcNow;
}

/// <summary>The stable <c>code</c> of <c>obs apply</c>.</summary>
public static class ObsApplyCodes
{
    /// <summary>The merge was written (or only the record, when the file already had it).</summary>
    public const string Applied = "obs.applied";

    /// <summary>Without <c>--backup</c>: the merge can be written; nothing was.</summary>
    public const string Ready = "obs.apply_ready";

    /// <summary>The live collection already has the template's changes; nothing to write.</summary>
    public const string InSync = "obs.apply_in_sync";

    /// <summary>The template and the operator changed the same value. Not ok.</summary>
    public const string Conflict = "obs.apply_conflict";

    /// <summary>The template the collection was last written from is not known. Not ok.</summary>
    public const string BaseUnknown = "obs.apply_base_unknown";

    /// <summary><c>--backup</c> while OBS runs: OBS saves its collection over the file. Not ok.</summary>
    public const string ObsRunning = "obs.apply_obs_running";

    /// <summary>A release rollback waits to put a backup back over this collection. Not ok.</summary>
    public const string RollbackPending = "obs.apply_rollback_pending";

    /// <summary>The merged collection did not compare as the template plus the operator's work. Not ok.</summary>
    public const string Unverified = "obs.apply_unverified";

    /// <summary>The backup or the write failed; the collection is as it was. Not ok.</summary>
    public const string Failed = "obs.apply_failed";

    /// <summary>There is no live collection to merge into. Not ok.</summary>
    public const string CollectionMissing = ObsPlanCodes.Missing;

    /// <summary>The live collection cannot be read or is not JSON. Not ok.</summary>
    public const string CollectionUnreadable = ObsPlanCodes.Unreadable;

    /// <summary>The install has no <c>obs\Default.json</c>. Not ok.</summary>
    public const string TemplateMissing = ObsPlanCodes.TemplateMissing;
}

/// <summary>The <c>obs apply</c> result envelope, written by the shared <see cref="CliJson"/> serializer (#311).</summary>
public sealed record ObsApplyResult : ICliResult
{
    public const int CurrentSchemaVersion = CliJson.SchemaVersion;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool Ok { get; init; }

    /// <summary>One of <see cref="ObsApplyCodes"/>.</summary>
    public string Code { get; init; }

    public string Message { get; init; }

    public string Collection { get; init; }

    public string Template { get; init; }

    public string TemplateSha256 { get; init; }

    /// <summary>The template the live collection was last written from (<c>managed-collections.json</c>).</summary>
    public string RecordedTemplateSha256 { get; init; }

    /// <summary>Where the base came from: <c>install</c>, <c>previous</c>, <c>stored</c>, or <c>none</c>.</summary>
    public string Base { get; init; }

    public bool ObsRunning { get; init; }

    /// <summary>The live collection file was written.</summary>
    public bool Written { get; init; }

    /// <summary>The backup the write took: <c>obs restore</c> with it undoes the apply.</summary>
    public string Backup { get; init; }

    /// <summary>How many differences the merge takes from the template.</summary>
    public int Taken { get; init; }

    /// <summary>How many of the operator's differences the merge keeps.</summary>
    public int Kept { get; init; }

    /// <summary>Every difference, as <c>obs plan</c> shows it.</summary>
    public IReadOnlyList<ObsCollectionDifference> Differences { get; init; } = [];

    /// <summary>The differences that refused the merge.</summary>
    public IReadOnlyList<ObsCollectionDifference> Blocking { get; init; } = [];

    public string ToJson() => CliJson.Serialize(this);
}

/// <summary>
/// What <c>obs restore</c> needs to undo an <c>obs apply</c> completely: the record of the
/// collection from before it, which describes the backup the apply took.
/// </summary>
public sealed record ObsApplyUndo
{
    public string CollectionPath { get; init; }

    /// <summary>The backup the apply's write took.</summary>
    public string Backup { get; init; }

    /// <summary>The collection's record before the apply, or null when it had none.</summary>
    public ObsManagedCollection PreviousRecord { get; init; }

    public DateTime AppliedAtUtc { get; init; }
}

/// <summary>
/// <c>obs apply</c> (#307): merges the install's template changes into the live collection, three
/// way, and keeps every override, addition, and removal of the operator's
/// (<see cref="ObsCollectionMerge"/>). Without <see cref="ObsApplyRequest.Write"/> it only
/// reports the merge. With it, and only while OBS is closed, the collection is backed up and the
/// merge written through <see cref="ObsFileTransaction"/>, the record names this template (and
/// that the collection keeps the operator's work, so no update replaces it), and
/// <see cref="ObsApplyUndo"/> lets <c>obs restore</c> put the record back with the backup.
/// </summary>
public static class ObsCollectionApply
{
    public static ObsApplyResult Run(ObsApplyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Managed);
        ObsManagedFiles managed = request.Managed;
        string collection = string.IsNullOrWhiteSpace(request.CollectionPath)
            ? null
            : Path.GetFullPath(request.CollectionPath);
        var result = new ObsApplyResult
        {
            Collection = collection,
            Template = request.TemplatePath,
            ObsRunning = request.ObsIsRunning,
            Base = "none",
        };

        string template = ReadText(request.TemplatePath);
        if (template == null || collection == null)
        {
            return result with
            {
                Code = ObsApplyCodes.TemplateMissing,
                Message =
                    "There is no OBS collection template at "
                    + (request.TemplatePath ?? "(none)")
                    + ", so there is nothing to merge.",
            };
        }

        string templateHash = ObsCollectionPatcher.HashOf(template);
        ObsManagedCollection record = managed.Read(collection);
        result = result with
        {
            TemplateSha256 = templateHash,
            RecordedTemplateSha256 = record?.TemplateSha256,
        };
        if (request.Write && request.ObsIsRunning)
        {
            return result with
            {
                Code = ObsApplyCodes.ObsRunning,
                Message =
                    "OBS is running and saves its collection over the file, so nothing was merged. Close OBS and run it again (without --backup it only shows the merge, and works while OBS runs).",
            };
        }

        if (!File.Exists(collection))
        {
            return result with
            {
                Code = ObsApplyCodes.CollectionMissing,
                Message =
                    "There is no live collection at "
                    + collection
                    + " to merge into. services start or update install-obs creates it from the template.",
            };
        }

        ObsPendingRestore pending = managed.ReadPendingRestore();
        if (pending != null && ObsManagedFiles.SamePath(pending.CollectionPath, collection))
        {
            return result with
            {
                Code = ObsApplyCodes.RollbackPending,
                Message =
                    ObsCollectionRollback.DescribePending(managed)
                    + " Nothing was merged: let the rollback finish first, or close OBS and obs restore a backup by hand.",
            };
        }

        string live = ReadText(collection);

        // The assets point where an update points them: the stable copy with OBS:StableAssets
        // (named here, made only before a write), else the install's obs folder (#330).
        string install = Path.GetDirectoryName(Path.GetFullPath(request.TemplatePath));
        ObsAssetStore store = request.AssetStore ?? ObsAssetStore.For(managed);
        string assetRoot = request.StableAssets ? store.Planned(install) ?? install : install;
        var movedFrom = new List<string> { store.AnyCopy };
        if (!ObsManagedFiles.SamePath(assetRoot, install))
        {
            movedFrom.Add(install);
        }

        ObsCollectionBase found = ObsCollectionMerge.FindBase(
            record,
            template,
            templateHash,
            ReadText(request.PreviousTemplatePath),
            managed
        );
        ObsMergeResult merge;
        try
        {
            merge = ObsCollectionMerge.Merge(
                ObsCollectionMerge.Normalize(
                    found.Text,
                    assetRoot,
                    request.DataDirectory,
                    movedFrom
                ),
                ObsCollectionMerge.Normalize(template, assetRoot, request.DataDirectory, movedFrom),
                ObsCollectionMerge.Normalize(
                    live ?? throw new IOException("It could not be read."),
                    assetRoot,
                    request.DataDirectory,
                    movedFrom
                ),
                request.Runtime,
                ObsMergeScope.KeepOperatorChanges
            );
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return result with
            {
                Code = ObsApplyCodes.CollectionUnreadable,
                Message = "The live collection " + collection + " cannot be merged: " + e.Message,
            };
        }

        result = result with
        {
            Base = found.Source,
            Differences = merge.Diff.Differences,
            Blocking = merge.Blocking,
            Taken = merge.Taken,
            Kept = merge.Kept,
        };
        if (merge.Outcome == ObsMergeOutcome.BaseUnknown && merge.Diff.Differences.Count == 0)
        {
            // No base, but nothing differs either: the collection is the template.
            merge = merge with
            {
                Outcome = ObsMergeOutcome.Merged,
                Merged = ObsCollectionMerge.Normalize(
                    live,
                    assetRoot,
                    request.DataDirectory,
                    movedFrom
                ),
            };
        }

        if (!merge.Ok)
        {
            return result with
            {
                Code = merge.Outcome switch
                {
                    ObsMergeOutcome.Conflict => ObsApplyCodes.Conflict,
                    ObsMergeOutcome.BaseUnknown => ObsApplyCodes.BaseUnknown,
                    _ => ObsApplyCodes.Unverified,
                },
                Message =
                    merge.Message
                    + (
                        merge.Outcome == ObsMergeOutcome.BaseUnknown
                            ? " The record in managed-collections.json names "
                                + (record?.TemplateSha256 ?? "no template")
                                + ", which is neither this install's template, --previous's, nor stored in "
                                + managed.TemplateDirectory
                                + "."
                            : string.Empty
                    ),
            };
        }

        bool write = !string.Equals(merge.Merged, live, StringComparison.Ordinal);
        bool keeps = merge.Kept > 0;
        List<string> names = ObsCollectionPaths
            .SourceNames(merge.Merged)
            .Order(StringComparer.Ordinal)
            .ToList();
        bool recorded =
            record != null
            && string.Equals(record.TemplateSha256, templateHash, StringComparison.Ordinal)
            && record.Merged == keeps
            && record.Sources.Order(StringComparer.Ordinal).SequenceEqual(names);
        if (!write && recorded)
        {
            return result with
            {
                Ok = true,
                Code = ObsApplyCodes.InSync,
                Message =
                    "The live collection already has this install's template changes"
                    + (keeps ? $" and keeps {merge.Kept} of the operator's" : string.Empty)
                    + ". Nothing was written.",
            };
        }

        string what =
            $"takes {merge.Taken} template change(s) and keeps {merge.Kept} of the operator's"
            + (write ? string.Empty : " (the file already has them; only its record changes)");
        string worktree =
            write && ObsCollectionPaths.IsEphemeral(assetRoot)
                ? "Refusing to point OBS at "
                    + assetRoot
                    + ": it is inside a git worktree, which is removed with it (#330). Turn on OBS:StableAssets, or run it from a stable install."
                : null;
        if (!request.Write)
        {
            return result with
            {
                Ok = true,
                Code = ObsApplyCodes.Ready,
                Message =
                    "The merge "
                    + what
                    + ". Nothing was written: run it with --backup, with OBS closed, to write it."
                    + (request.ObsIsRunning ? " OBS is running now." : string.Empty)
                    + (worktree == null ? string.Empty : " It would be refused: " + worktree),
            };
        }

        if (worktree != null)
        {
            return result with
            {
                Code = ObsApplyCodes.Failed,
                Message = worktree + " Nothing was written.",
            };
        }

        if (write && request.StableAssets)
        {
            // The merge points at the stable copy: make and check it before OBS can load the file.
            ObsAssetCopy copy = store.Ensure(install, request.UtcNow);
            if (!copy.Ok || !ObsManagedFiles.SamePath(copy.AssetRoot, assetRoot))
            {
                return result with
                {
                    Code = ObsApplyCodes.Failed,
                    Message =
                        copy.Message
                        + " The merge points at "
                        + assetRoot
                        + ", so nothing was written.",
                };
            }
        }

        string backup;
        try
        {
            managed.SaveTemplate(template, request.UtcNow);
            backup = write
                ? ObsFileTransaction.Write(
                    collection,
                    merge.Merged,
                    managed.BackupDirectory,
                    request.UtcNow
                )
                : null;
            managed.Save(
                collection,
                new ObsManagedCollection(templateHash, names, request.UtcNow, keeps)
            );
            if (backup != null)
            {
                managed.SaveApplyUndo(
                    new ObsApplyUndo
                    {
                        CollectionPath = collection,
                        Backup = backup,
                        PreviousRecord = record,
                        AppliedAtUtc = request.UtcNow.ToUniversalTime(),
                    }
                );
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return result with
            {
                Code = ObsApplyCodes.Failed,
                Message =
                    "The merge was not written, and the collection is as it was. " + e.Message,
            };
        }

        return result with
        {
            Ok = true,
            Code = ObsApplyCodes.Applied,
            Written = write,
            Backup = backup,
            Message =
                "Merged: the collection "
                + what
                + ". managed-collections.json names this template"
                + (
                    keeps
                        ? " and that the collection keeps the operator's work, so no update replaces it"
                        : string.Empty
                )
                + "."
                + (
                    backup == null
                        ? string.Empty
                        : " The collection before it was saved to "
                            + backup
                            + ". To undo, close OBS and run heroesreplay obs restore \""
                            + Path.GetFileName(backup)
                            + "\" (it puts the record back too)."
                ),
        };
    }

    /// <summary>
    /// After <c>obs restore</c> put <paramref name="backup"/> back: when it is the backup the last
    /// <c>obs apply</c> took, the collection's record goes back to what it was then. Returns one
    /// line for the restore's message, or null when it was another backup.
    /// </summary>
    public static string Undo(ObsManagedFiles managed, string collectionPath, string backup)
    {
        ArgumentNullException.ThrowIfNull(managed);
        ObsApplyUndo undo = managed.ReadApplyUndo();
        if (
            undo == null
            || !ObsManagedFiles.SamePath(undo.CollectionPath, collectionPath)
            || !string.Equals(
                Path.GetFileName(undo.Backup),
                Path.GetFileName(backup),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return null;
        }

        managed.Put(collectionPath, undo.PreviousRecord);
        managed.ClearApplyUndo();
        return "This was the backup obs apply took, so managed-collections.json is back to its entry from before it.";
    }

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
}
