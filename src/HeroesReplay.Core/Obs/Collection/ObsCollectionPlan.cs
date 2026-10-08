using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Obs.Collection;

/// <summary>What <see cref="ObsCollectionPlan.Build"/> compares.</summary>
public sealed record ObsCollectionPlanRequest
{
    /// <summary>The <c>obs\Default.json</c> an update would install.</summary>
    public string TemplatePath { get; init; }

    /// <summary>
    /// The <c>obs\Default.json</c> of the install running now, when the plan previews a newer one.
    /// It is the base when the live collection was written from it.
    /// </summary>
    public string PreviousTemplatePath { get; init; }

    /// <summary>The live collection, <c>%APPDATA%\obs-studio\basic\scenes\&lt;name&gt;.json</c>.</summary>
    public string CollectionPath { get; init; }

    public string CollectionName { get; init; }

    /// <summary>The effective <c>Location:DataDirectory</c>, for the path rewrite.</summary>
    public string DataDirectory { get; init; }

    public ObsManagedFiles Managed { get; init; }

    public bool ObsIsRunning { get; init; }

    /// <summary><c>OBS:LiveCollectionSwap</c>: a deferred replacement goes in at the next replay.</summary>
    public bool LiveCollectionSwap { get; init; } = true;

    /// <summary>Values the spectator sets per replay (<see cref="ObsRuntimeValues.From"/>): not compared.</summary>
    public ObsRuntimeValues Runtime { get; init; }

    public DateTime UtcNow { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// The folder an update would point the assets at: with <c>OBS:StableAssets</c>, the stable
    /// copy (<see cref="ObsAssetStore.Planned"/>), which the plan does not make. Null: the
    /// template's folder.
    /// </summary>
    public string AssetRoot { get; init; }
}

/// <summary>What <c>update install-obs</c> would do with the live collection now.</summary>
public sealed record ObsPlanUpdate
{
    /// <summary>
    /// <c>none</c>, <c>create</c>, <c>replace</c> (the whole collection, with the template),
    /// <c>update_paths</c>, <c>restore</c> (a release rollback that waits), or <c>keep</c>
    /// (custom or unreadable).
    /// </summary>
    public string Action { get; init; }

    /// <summary>It waits for OBS: at the next replay through the live swap, or once OBS is closed.</summary>
    public bool Deferred { get; init; }

    /// <summary>A deferred replacement goes in at the spectator's next replay without a stream stop.</summary>
    public bool LiveSwap { get; init; }

    public string Message { get; init; }
}

/// <summary>
/// The <c>obs plan</c> result envelope (#307). <see cref="Ok"/> is false only when the update
/// cannot be planned (no template, an unreadable live collection) or a value has a conflict.
/// </summary>
public sealed record ObsCollectionPlanResult
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool Ok { get; init; }

    /// <summary>One of <see cref="ObsPlanCodes"/>.</summary>
    public string Code { get; init; }

    public string Message { get; init; }

    public string Collection { get; init; }

    public string Template { get; init; }

    public string TemplateSha256 { get; init; }

    /// <summary>The template the live collection was last written from (<c>managed-collections.json</c>).</summary>
    public string RecordedTemplateSha256 { get; init; }

    /// <summary>
    /// Where the base came from: <c>install</c> (the live collection was written from this
    /// template), <c>previous</c> (from <c>--previous</c>), or <c>none</c>.
    /// </summary>
    public string Base { get; init; }

    public bool ObsRunning { get; init; }

    public ObsPlanUpdate Update { get; init; }

    /// <summary>A release rollback that waits for OBS (<c>restore-pending.json</c>), or null.</summary>
    public string PendingRollback { get; init; }

    /// <summary>How many differences of each kind.</summary>
    public IReadOnlyDictionary<string, int> Summary { get; init; } = new Dictionary<string, int>();

    public IReadOnlyList<ObsCollectionDifference> Differences { get; init; } = [];

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static ObsCollectionPlanResult FromJson(string json) =>
        JsonSerializer.Deserialize<ObsCollectionPlanResult>(json, Json);
}

/// <summary>The stable <c>code</c> of an <c>obs plan</c>.</summary>
public static class ObsPlanCodes
{
    /// <summary>The live collection equals the template, and an update writes nothing.</summary>
    public const string InSync = "obs.plan_in_sync";

    /// <summary>There are differences or a write is due, and nothing conflicts.</summary>
    public const string Changes = "obs.plan_changes";

    /// <summary>The template and the operator changed the same value. Not ok.</summary>
    public const string Conflict = "obs.plan_conflict";

    /// <summary>The live collection differs and its base template is not known.</summary>
    public const string BaseUnknown = "obs.plan_base_unknown";

    /// <summary>The scenes and sources are not the managed ones: an update keeps the collection.</summary>
    public const string Custom = "obs.collection_custom";

    /// <summary>There is no live collection: an update creates it.</summary>
    public const string Missing = "obs.collection_missing";

    /// <summary>The live collection cannot be read or is not JSON. Not ok.</summary>
    public const string Unreadable = "obs.collection_unreadable";

    /// <summary>The install has no <c>obs\Default.json</c>. Not ok.</summary>
    public const string TemplateMissing = "obs.template_missing";
}

/// <summary>
/// <c>obs plan</c> (#307): what an update would change in the live scene collection, read from
/// files only, so it is safe while OBS runs. It never writes the live collection or
/// <c>%LOCALAPPDATA%\HeroesReplay\obs</c>: the update's own decision
/// (<see cref="ObsCollectionPatcher.Apply"/>, as <c>update install-obs</c> runs it) is made on
/// copies in a temp folder, and the structured diff (<see cref="ObsCollectionDiff"/>) shows each
/// difference and what a merge that keeps the operator's work would do with it.
/// </summary>
public static class ObsCollectionPlan
{
    public static ObsCollectionPlanResult Build(ObsCollectionPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Managed);
        string collection = string.IsNullOrWhiteSpace(request.CollectionPath)
            ? null
            : Path.GetFullPath(request.CollectionPath);
        var result = new ObsCollectionPlanResult
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
                Ok = false,
                Code = ObsPlanCodes.TemplateMissing,
                Message =
                    "There is no OBS collection template at "
                    + (request.TemplatePath ?? "(none)")
                    + ", so there is nothing to plan.",
            };
        }

        string templateHash = ObsCollectionPatcher.TemplateHash(request.TemplatePath);
        ObsManagedCollection record = request.Managed.Read(collection);
        ObsPendingRestore pending = request.Managed.ReadPendingRestore();
        bool pendingHere =
            pending != null && ObsManagedFiles.SamePath(pending.CollectionPath, collection);
        result = result with
        {
            TemplateSha256 = templateHash,
            RecordedTemplateSha256 = record?.TemplateSha256,
            PendingRollback = pendingHere
                ? ObsCollectionRollback.DescribePending(request.Managed)
                : null,
        };

        ObsPlanUpdate update = DryRun(
            request,
            collection,
            templateHash,
            record,
            pending,
            pendingHere
        );
        result = result with { Update = update };
        if (!File.Exists(collection))
        {
            return result with
            {
                Ok = true,
                Code = ObsPlanCodes.Missing,
                Message = "There is no live collection at " + collection + ". " + update.Message,
            };
        }

        string live = ReadText(collection);
        (string baseTemplate, string baseName) = Base(request, record, template, templateHash);
        (string assetRoot, IReadOnlyList<string> movedFrom) = Assets(request);
        ObsCollectionDiffResult diff;
        try
        {
            diff = ObsCollectionDiff.Compare(
                Normalize(baseTemplate, assetRoot, request.DataDirectory, movedFrom),
                Normalize(template, assetRoot, request.DataDirectory, movedFrom),
                Normalize(
                    live ?? throw new IOException("It could not be read."),
                    assetRoot,
                    request.DataDirectory,
                    movedFrom
                ),
                request.Runtime
            );
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return result with
            {
                Ok = false,
                Code = ObsPlanCodes.Unreadable,
                Message = "The live collection " + collection + " cannot be compared: " + e.Message,
            };
        }

        var summary = Enum.GetValues<ObsDiffKind>()
            .ToDictionary(
                kind => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()),
                diff.Count
            );
        string code =
            diff.HasConflicts ? ObsPlanCodes.Conflict
            : update.Action == "keep" ? ObsPlanCodes.Custom
            : diff.Count(ObsDiffKind.Unattributed) > 0 ? ObsPlanCodes.BaseUnknown
            : diff.Differences.Count > 0 || update.Action != "none" ? ObsPlanCodes.Changes
            : ObsPlanCodes.InSync;
        return result with
        {
            Ok = !diff.HasConflicts,
            Code = code,
            Base = baseName,
            Summary = summary,
            Differences = diff.Differences,
            Message = Summarize(diff) + " " + update.Message,
        };
    }

    /// <summary>
    /// The template the live collection was last written from, when this install or the
    /// previous one has it.
    /// </summary>
    private static (string Template, string Name) Base(
        ObsCollectionPlanRequest request,
        ObsManagedCollection record,
        string template,
        string templateHash
    )
    {
        if (record == null)
        {
            return (null, "none");
        }

        if (string.Equals(record.TemplateSha256, templateHash, StringComparison.Ordinal))
        {
            return (template, "install");
        }

        if (
            string.Equals(
                record.TemplateSha256,
                ObsCollectionPatcher.TemplateHash(request.PreviousTemplatePath),
                StringComparison.Ordinal
            )
        )
        {
            return (ReadText(request.PreviousTemplatePath), "previous");
        }

        return (null, "none");
    }

    /// <summary>
    /// The update's own decision, made by <see cref="ObsCollectionPatcher.Apply"/> on copies of
    /// the live collection, its record, and a waiting rollback in a temp folder.
    /// </summary>
    private static ObsPlanUpdate DryRun(
        ObsCollectionPlanRequest request,
        string collection,
        string templateHash,
        ObsManagedCollection record,
        ObsPendingRestore pending,
        bool pendingHere
    )
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-obs-plan-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            string scenes = Path.Combine(sandbox, "scenes");
            Directory.CreateDirectory(scenes);
            string copy = Path.Combine(scenes, Path.GetFileName(collection));
            bool existed = File.Exists(collection);
            if (existed)
            {
                File.Copy(collection, copy);
            }

            var managed = new ObsManagedFiles(Path.Combine(sandbox, "managed"));
            if (record != null)
            {
                managed.Save(copy, record);
            }

            bool restoring =
                pendingHere
                && string.Equals(pending.TemplateSha256, templateHash, StringComparison.Ordinal);
            if (pendingHere)
            {
                managed.SavePendingRestore(pending with { CollectionPath = copy });
            }

            ObsCollectionApplyResult applied = ObsCollectionPatcher.Apply(
                new ObsCollectionUpdate
                {
                    TemplatePath = request.TemplatePath,
                    DestinationPath = copy,
                    DataDirectory = request.DataDirectory,
                    ObsIsRunning = request.ObsIsRunning,
                    CollectionName = request.CollectionName,
                    Managed = managed,
                    Release = true,
                    PreviousTemplatePath = request.PreviousTemplatePath,
                    UtcNow = request.UtcNow,
                    // The copy the update would use, named but not made; the real store's
                    // copies are recognised as older asset folders.
                    AssetRoot = Assets(request).Root,
                    AssetStore = ObsAssetStore.For(request.Managed),
                }
            );
            string action =
                applied.Drift ? "keep"
                : !applied.Wrote && !applied.Deferred ? "none"
                : restoring ? "restore"
                : !existed ? "create"
                : applied.Replacement != null
                || !string.Equals(record?.TemplateSha256, templateHash, StringComparison.Ordinal)
                    ? "replace"
                : "update_paths";
            var update = new ObsPlanUpdate
            {
                Action = action,
                Deferred = applied.Deferred,
                LiveSwap =
                    applied.Deferred && applied.Replacement != null && request.LiveCollectionSwap,
            };

            // The patcher's own words are kept only for a collection it keeps (why it is custom).
            return update with
            {
                Message = applied.Drift
                    ? "An update keeps the collection as it is. "
                        + applied.Message.Replace(
                            sandbox,
                            "(plan)",
                            StringComparison.OrdinalIgnoreCase
                        )
                    : Describe(update),
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ObsPlanUpdate
            {
                Action = "keep",
                Message = "The update's decision could not be worked out: " + e.Message,
            };
        }
        finally
        {
            try
            {
                Directory.Delete(sandbox, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover temp folder changes nothing.
            }
        }
    }

    private static string Describe(ObsPlanUpdate update)
    {
        string what = update.Action switch
        {
            "create" => "An update creates the collection from the template",
            "replace" => "An update replaces the whole collection with the template",
            "update_paths" => "An update points the asset and data paths at this install",
            "restore" => "An update puts back the collection a release rollback waits to restore",
            "keep" => "An update keeps the collection as it is",
            _ => "An update writes nothing",
        };
        string when =
            !update.Deferred ? "."
            : update.LiveSwap ? " at the next replay, through the live swap (OBS is running)."
            : " once HeroesReplay finds OBS closed (OBS is running).";
        return what + when;
    }

    private static string Summarize(ObsCollectionDiffResult diff)
    {
        if (diff.Differences.Count == 0)
        {
            return "The live collection matches the template.";
        }

        IEnumerable<string> parts = Enum.GetValues<ObsDiffKind>()
            .Where(kind => diff.Count(kind) > 0)
            .Select(kind => $"{diff.Count(kind)} {Words(kind)}");
        return $"{diff.Differences.Count} difference(s): {string.Join(", ", parts)}."
            + (
                diff.BaseKnown
                    ? string.Empty
                    : " The template the live collection was written from is not known, so the differences are not attributed."
            );
    }

    internal static string Words(ObsDiffKind kind) =>
        kind switch
        {
            ObsDiffKind.ManagedChange => "managed change",
            ObsDiffKind.ManagedAddition => "managed addition",
            ObsDiffKind.ManagedRemoval => "managed removal",
            ObsDiffKind.OperatorOverride => "operator override",
            ObsDiffKind.OperatorAddition => "operator addition",
            ObsDiffKind.OperatorRemoval => "operator removal",
            ObsDiffKind.Conflict => "conflict",
            _ => "unattributed",
        };

    private static string Normalize(
        string json,
        string assetRoot,
        string dataDirectory,
        IReadOnlyList<string> movedFrom
    ) =>
        json == null ? null : ObsCollectionPaths.Rewrite(json, assetRoot, dataDirectory, movedFrom);

    /// <summary>The asset root an update would use, and the folders its path update moves from.</summary>
    private static (string Root, IReadOnlyList<string> MovedFrom) Assets(
        ObsCollectionPlanRequest request
    )
    {
        string install = Path.GetDirectoryName(Path.GetFullPath(request.TemplatePath));
        string root = string.IsNullOrWhiteSpace(request.AssetRoot)
            ? install
            : Path.GetFullPath(request.AssetRoot);
        var movedFrom = new List<string> { ObsAssetStore.For(request.Managed).AnyCopy };
        if (!ObsManagedFiles.SamePath(root, install))
        {
            movedFrom.Add(install);
        }

        return (root, movedFrom);
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
