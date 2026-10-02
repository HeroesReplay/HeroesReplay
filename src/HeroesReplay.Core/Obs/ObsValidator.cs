using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// One validation result. <see cref="Code"/> is stable and <see cref="Message"/> says what to
/// change. A <see cref="Severity"/> of <c>error</c> fails the validation; <c>warning</c> does
/// not. <see cref="Subject"/> is the scene, source, file, or request it is about.
/// </summary>
public sealed record ObsFinding(string Code, string Severity, string Subject, string Message);

/// <summary>
/// The live OBS collection compared with the packaged <c>obs/Default.json</c> contract.
/// <see cref="Ok"/> is true when OBS was read and no finding is an error.
/// </summary>
public sealed record ObsValidation
{
    public int SchemaVersion => 1;
    public bool Ok { get; init; }

    /// <summary>The first error's code, or the reason OBS could not be read. Null when <see cref="Ok"/>.</summary>
    public string Code { get; init; }
    public string Error { get; init; }
    public string Endpoint { get; init; }
    public string PackagedCollection { get; init; }
    public string AssetRoot { get; init; }
    public string DataDirectory { get; init; }
    public int Errors { get; init; }
    public int Warnings { get; init; }
    public IReadOnlyList<ObsFinding> Findings { get; init; } = [];
}

/// <summary>
/// Read-only checks of the collection OBS has loaded: the requests HeroesReplay sends, the
/// active profile and collection (<see cref="ObsSelection"/>), the scenes and sources it drives
/// (<see cref="ObsContract"/>), source kinds and scene-item placement against
/// <c>obs/Default.json</c>, local asset paths after <see cref="ObsCollectionPaths.RewriteValue"/>,
/// and the Mic/Aux global input.
/// </summary>
public static class ObsValidator
{
    public const string Error = "error";
    public const string Warning = "warning";

    public const string BundleMissing = "obs.bundle_missing";
    public const string BundleInvalid = "obs.bundle_invalid";
    public const string AssetMissing = "obs.asset_missing";
    public const string RequestUnavailable = "obs.request_unavailable";
    public const string SceneMissing = "obs.scene_missing";
    public const string SourceMissing = "obs.source_missing";
    public const string SourceKindMismatch = "obs.source_kind_mismatch";
    public const string SceneItemMissing = "obs.scene_item_missing";
    public const string CollectionCustom = "obs.collection_custom";
    public const string FileMissing = "obs.file_missing";
    public const string RuntimeFileMissing = "obs.runtime_file_missing";
    public const string PathStale = "obs.path_stale";
    public const string UrlInvalid = "obs.url_invalid";
    public const string MicEnabled = "obs.mic_enabled";
    public const string MicMuted = "obs.mic_muted";

    /// <summary>The obs-websocket requests HeroesReplay sends while it spectates.</summary>
    public static readonly IReadOnlyList<string> RequiredRequests =
    [
        "GetVersion",
        "GetProfileList",
        "GetSceneCollectionList",
        "GetCurrentProgramScene",
        "SetCurrentProgramScene",
        "GetInputList",
        "GetInputSettings",
        "SetInputSettings",
        "GetSceneItemId",
        "SetSceneItemEnabled",
        "SetSourceFilterSettings",
        "SetRecordDirectory",
        "GetRecordStatus",
        "StartRecord",
        "StopRecord",
        "GetStreamStatus",
        "StartStream",
        "StopStream",
    ];

    private static readonly string[] MicSlots = { "mic1", "mic2", "mic3", "mic4" };

    public static ObsValidation Unavailable(
        ObsInspectionSettings settings,
        string code,
        string error
    ) =>
        new()
        {
            Ok = false,
            Code = code,
            Error = error,
            Endpoint = settings?.Obs?.WebSocketEndpoint,
            DataDirectory = settings?.DataDirectory,
        };

    public static ObsValidation Validate(IObsReadSession session, ObsInspectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(session);
        var findings = new List<ObsFinding>();
        Packaged packaged = ReadPackaged(settings?.InstallDirectory, findings);

        CheckRequests(session.Get("GetVersion"), findings);
        CheckSelection(
            settings?.Obs,
            session.Get("GetProfileList"),
            session.Get("GetSceneCollectionList"),
            findings
        );

        IReadOnlyList<string> scenes = ObsResponse
            .Objects(session.Get("GetSceneList"), "scenes")
            .Select(scene => ObsResponse.String(scene, "sceneName"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        IReadOnlyDictionary<string, string> global = ObsInspector.GlobalAudio(
            session.Get("GetSpecialInputs")
        );
        List<JObject> inputs = ObsResponse
            .Objects(session.Get("GetInputList"), "inputs")
            .Where(input => !string.IsNullOrWhiteSpace(ObsResponse.String(input, "inputName")))
            .ToList();

        CheckContract(session, ObsContract.From(settings?.Obs), scenes, inputs, packaged, findings);
        CheckDrift(
            packaged,
            ObsNames.SceneCollection(settings?.Obs),
            scenes,
            inputs,
            global,
            findings
        );
        CheckPaths(session, inputs, global, packaged.AssetRoot, settings?.DataDirectory, findings);
        CheckMicrophone(session, global, findings);

        List<ObsFinding> ordered = findings
            .OrderBy(finding => finding.Severity == Error ? 0 : 1)
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .ThenBy(finding => finding.Subject, StringComparer.Ordinal)
            .ToList();
        ObsFinding firstError = ordered.FirstOrDefault(finding => finding.Severity == Error);
        return new ObsValidation
        {
            Ok = firstError == null,
            Code = firstError?.Code,
            Endpoint = settings?.Obs?.WebSocketEndpoint,
            PackagedCollection = packaged.Path,
            AssetRoot = packaged.AssetRoot,
            DataDirectory = settings?.DataDirectory,
            Errors = ordered.Count(finding => finding.Severity == Error),
            Warnings = ordered.Count(finding => finding.Severity == Warning),
            Findings = ordered,
        };
    }

    private sealed record Packaged(
        string Path,
        string AssetRoot,
        IReadOnlyList<string> Names,
        IReadOnlyDictionary<string, string> Kinds
    );

    private static Packaged ReadPackaged(string installDirectory, List<ObsFinding> findings)
    {
        string path = ObsCollectionPaths.FindCollection(installDirectory);
        if (path == null)
        {
            findings.Add(
                new ObsFinding(
                    BundleMissing,
                    Error,
                    "obs/Default.json",
                    "The packaged collection obs/Default.json was not found in or above "
                        + installDirectory
                        + ". Source kinds, asset paths, and drift were not compared."
                )
            );
            return new Packaged(null, null, null, null);
        }

        string assetRoot = Path.GetDirectoryName(path);
        try
        {
            string json = File.ReadAllText(path);
            foreach (string asset in ObsCollectionPaths.MissingAssets(assetRoot, json))
            {
                findings.Add(
                    new ObsFinding(
                        AssetMissing,
                        Error,
                        asset,
                        "The install is missing obs/" + asset + ", which the collection uses."
                    )
                );
            }

            return new Packaged(
                path,
                assetRoot,
                ObsCollectionPaths.SourceNames(json),
                ObsCollectionPaths.SourceKinds(json)
            );
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            findings.Add(
                new ObsFinding(
                    BundleInvalid,
                    Error,
                    path,
                    "The packaged collection could not be read. " + e.Message
                )
            );
            return new Packaged(path, assetRoot, null, null);
        }
    }

    private static void CheckRequests(JObject version, List<ObsFinding> findings)
    {
        var available = new HashSet<string>(
            ObsResponse.Strings(version, "availableRequests"),
            StringComparer.Ordinal
        );
        foreach (string request in RequiredRequests.Where(name => !available.Contains(name)))
        {
            findings.Add(
                new ObsFinding(
                    RequestUnavailable,
                    Error,
                    request,
                    "obs-websocket "
                        + ObsResponse.String(version, "obsWebSocketVersion")
                        + " does not offer "
                        + request
                        + ", which HeroesReplay sends. Update OBS Studio."
                )
            );
        }
    }

    private static void CheckSelection(
        OBSSettings obs,
        JObject profiles,
        JObject collections,
        List<ObsFinding> findings
    )
    {
        string profile = ObsNames.Profile(obs);
        string collection = ObsNames.SceneCollection(obs);
        string activeProfile = ObsResponse.String(profiles, "currentProfileName");
        string activeCollection = ObsResponse.String(collections, "currentSceneCollectionName");
        ObsSelectionResult selection = ObsSelection.Check(
            profile,
            collection,
            activeProfile,
            activeCollection
        );
        if (selection.Ok)
        {
            return;
        }

        if (selection.Reason == ObsSelection.Unreadable)
        {
            findings.Add(
                new ObsFinding(
                    ObsSelection.Unreadable,
                    Error,
                    profile + " / " + collection,
                    "OBS did not report the active profile and scene collection. HeroesReplay starts no stream or recording until it can."
                )
            );
            return;
        }

        // A wrong profile and a wrong collection are two separate fixes.
        if (!string.Equals(activeProfile, profile, StringComparison.Ordinal))
        {
            findings.Add(
                new ObsFinding(
                    ObsSelection.ProfileMismatch,
                    Error,
                    profile,
                    "OBS profile is '"
                        + activeProfile
                        + "', expected '"
                        + profile
                        + "' (OBS:ProfileName). HeroesReplay starts no stream or recording until they match. Select Profile > "
                        + profile
                        + " in OBS, or change OBS:ProfileName."
                )
            );
        }

        if (!string.Equals(activeCollection, collection, StringComparison.Ordinal))
        {
            findings.Add(
                new ObsFinding(
                    ObsSelection.CollectionMismatch,
                    Error,
                    collection,
                    "OBS scene collection is '"
                        + activeCollection
                        + "', expected '"
                        + collection
                        + "' (OBS:SceneCollectionName). HeroesReplay starts no stream or recording until they match. Select Scene Collection > "
                        + collection
                        + " in OBS, or change OBS:SceneCollectionName."
                )
            );
        }
    }

    private static void CheckContract(
        IObsReadSession session,
        ObsContract contract,
        IReadOnlyList<string> scenes,
        IReadOnlyList<JObject> inputs,
        Packaged packaged,
        List<ObsFinding> findings
    )
    {
        foreach (string scene in ObsCollectionPaths.MissingNames(scenes, contract.Scenes))
        {
            findings.Add(
                new ObsFinding(
                    SceneMissing,
                    Error,
                    scene,
                    "OBS has no scene '"
                        + scene
                        + "', which HeroesReplay selects by name. Add it, or change the scene name in appsettings (OBS section)."
                )
            );
        }

        var byName = inputs.ToDictionary(
            input => ObsResponse.String(input, "inputName"),
            StringComparer.Ordinal
        );
        foreach (string source in contract.Sources)
        {
            if (!byName.TryGetValue(source, out JObject input))
            {
                findings.Add(
                    new ObsFinding(
                        SourceMissing,
                        Error,
                        source,
                        "OBS has no source '"
                            + source
                            + "', which HeroesReplay updates by name. Add it, or change the source name in appsettings (OBS section)."
                    )
                );
                continue;
            }

            string live = ObsResponse.String(input, "unversionedInputKind");
            if (
                packaged.Kinds != null
                && packaged.Kinds.TryGetValue(source, out string expected)
                && !string.IsNullOrWhiteSpace(live)
                && !string.Equals(live, expected, StringComparison.Ordinal)
            )
            {
                findings.Add(
                    new ObsFinding(
                        SourceKindMismatch,
                        Error,
                        source,
                        "Source '"
                            + source
                            + "' is a "
                            + live
                            + ", but obs/Default.json has a "
                            + expected
                            + ". HeroesReplay writes "
                            + expected
                            + " settings to it."
                    )
                );
            }
        }

        var liveScenes = new HashSet<string>(scenes, StringComparer.Ordinal);
        var itemsByScene = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (ObsContractItem item in contract.Items)
        {
            if (!liveScenes.Contains(item.Scene) || !byName.ContainsKey(item.Source))
            {
                // Already reported as a missing scene or source.
                continue;
            }

            if (!itemsByScene.TryGetValue(item.Scene, out HashSet<string> names))
            {
                names = new HashSet<string>(
                    (
                        ObsInspector.SceneItems(
                            session.Get(
                                "GetSceneItemList",
                                new JObject { ["sceneName"] = item.Scene }
                            )
                        ) ?? []
                    ).Select(sceneItem => sceneItem.Name),
                    StringComparer.Ordinal
                );
                itemsByScene[item.Scene] = names;
            }

            if (!names.Contains(item.Source))
            {
                findings.Add(
                    new ObsFinding(
                        SceneItemMissing,
                        Error,
                        item.Scene + "/" + item.Source,
                        "Scene '"
                            + item.Scene
                            + "' has no item '"
                            + item.Source
                            + "'. HeroesReplay shows and hides it there with GetSceneItemId."
                    )
                );
            }
        }
    }

    private static void CheckDrift(
        Packaged packaged,
        string collection,
        IReadOnlyList<string> scenes,
        IReadOnlyList<JObject> inputs,
        IReadOnlyDictionary<string, string> global,
        List<ObsFinding> findings
    )
    {
        if (packaged.Names == null)
        {
            return;
        }

        // Global audio devices are top-level keys in the collection file, not sources.
        IEnumerable<string> live = inputs
            .Select(input => ObsResponse.String(input, "inputName"))
            .Where(name => !global.ContainsKey(name))
            .Concat(scenes);
        ObsNameDrift drift = ObsCollectionPatcher.Drift(packaged.Names, live);
        if (!drift.Custom)
        {
            return;
        }

        findings.Add(
            new ObsFinding(
                CollectionCustom,
                Warning,
                collection,
                "The loaded collection does not have the same scenes and sources as obs/Default.json (extra: "
                    + Join(drift.Extra)
                    + "; missing: "
                    + Join(drift.Missing)
                    + "). The install treats it as custom and does not rewrite its asset paths."
            )
        );
    }

    private static void CheckPaths(
        IObsReadSession session,
        IReadOnlyList<JObject> inputs,
        IReadOnlyDictionary<string, string> global,
        string assetRoot,
        string dataDirectory,
        List<ObsFinding> findings
    )
    {
        foreach (JObject input in inputs)
        {
            string name = ObsResponse.String(input, "inputName");
            if (global.ContainsKey(name))
            {
                continue;
            }

            JObject settings;
            try
            {
                settings =
                    session
                        .Get("GetInputSettings", new JObject { ["inputName"] = name })
                        ?["inputSettings"] as JObject;
            }
            catch (ObsRequestException)
            {
                continue;
            }

            string kind = ObsResponse.String(input, "unversionedInputKind");
            foreach ((string property, string value) in References(kind, settings))
            {
                ObsFinding finding = CheckReference(
                    name,
                    property,
                    value,
                    assetRoot,
                    dataDirectory,
                    path => File.Exists(path) || Directory.Exists(path)
                );
                if (finding != null)
                {
                    findings.Add(finding);
                }
            }
        }
    }

    /// <summary>The settings OBS loads a file or page from, by input kind.</summary>
    internal static IEnumerable<(string Property, string Value)> References(
        string kind,
        JObject settings
    )
    {
        if (settings == null)
        {
            yield break;
        }

        switch (kind)
        {
            case "browser_source":
                yield return ObsResponse.Bool(settings, "is_local_file") == true
                    ? ("local_file", ObsResponse.String(settings, "local_file"))
                    : ("url", ObsResponse.String(settings, "url"));
                break;
            case "ffmpeg_source":
                // is_local_file defaults to true, so OBS leaves it out of the settings.
                if (ObsResponse.Bool(settings, "is_local_file") != false)
                {
                    yield return ("local_file", ObsResponse.String(settings, "local_file"));
                }

                break;
            case "text_gdiplus":
            case "text_ft2_source":
                if (ObsResponse.Bool(settings, "read_from_file") == true)
                {
                    yield return ("file", ObsResponse.String(settings, "file"));
                }

                break;
            default:
                yield return ("file", ObsResponse.String(settings, "file"));
                yield return ("local_file", ObsResponse.String(settings, "local_file"));
                break;
        }
    }

    /// <summary>
    /// A web URL must parse. A local path must exist; when <see cref="ObsCollectionPaths.RewriteValue"/>
    /// would point it somewhere else for this install, it is stale. A missing file under
    /// <c>Location:DataDirectory</c> is only a warning, because HeroesReplay writes it while it
    /// spectates. Null when the reference is fine.
    /// </summary>
    internal static ObsFinding CheckReference(
        string input,
        string property,
        string value,
        string assetRoot,
        string dataDirectory,
        Func<string, bool> exists
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string subject = input + "." + property;
        string trimmed = value.Trim();
        if (
            trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        )
        {
            // The URL is not echoed: a web page URL can carry an access token.
            return
                Uri.TryCreate(trimmed, UriKind.Absolute, out Uri url)
                && !string.IsNullOrEmpty(url.Host)
                ? null
                : new ObsFinding(
                    UrlInvalid,
                    Error,
                    subject,
                    "Source '" + input + "' has a " + property + " that is not a valid web URL."
                );
        }

        string local = LocalPath(trimmed);
        string expected = LocalPath(
            ObsCollectionPaths.RewriteValue(property, trimmed, assetRoot, dataDirectory)
        );
        bool stale = !string.Equals(local, expected, StringComparison.OrdinalIgnoreCase);
        bool rooted = Path.IsPathFullyQualified(local);
        if (rooted && exists(local))
        {
            return stale
                ? new ObsFinding(
                    PathStale,
                    Warning,
                    subject,
                    "Source '"
                        + input
                        + "' loads "
                        + local
                        + ", but this install's copy is "
                        + expected
                        + ". services start rewrites the collection paths while OBS is closed."
                )
                : null;
        }

        bool runtime = rooted && IsUnder(local, dataDirectory);
        string fix =
            stale
                ? " This install expects "
                    + expected
                    + (
                        Path.IsPathFullyQualified(expected) && exists(expected)
                            ? ", which exists"
                            : ", which is also missing"
                    )
                    + ". services start rewrites the collection paths while OBS is closed."
            : runtime ? " HeroesReplay writes it under Location:DataDirectory while it spectates."
            : string.Empty;
        return new ObsFinding(
            runtime ? RuntimeFileMissing : FileMissing,
            runtime ? Warning : Error,
            subject,
            "Source '" + input + "' loads " + local + ", which does not exist." + fix
        );
    }

    private static void CheckMicrophone(
        IObsReadSession session,
        IReadOnlyDictionary<string, string> global,
        List<ObsFinding> findings
    )
    {
        foreach ((string name, string slot) in global)
        {
            if (!MicSlots.Contains(slot, StringComparer.Ordinal))
            {
                continue;
            }

            bool? muted = ObsInspector.ReadMuted(session, name);
            findings.Add(
                muted == true
                    ? new ObsFinding(
                        MicMuted,
                        Warning,
                        name,
                        "Global audio device '"
                            + name
                            + "' ("
                            + slot
                            + ") is enabled but muted. Set Settings > Audio > Global Audio Devices > Mic/Auxiliary Audio to Disabled so an unmute cannot broadcast a microphone."
                    )
                    : new ObsFinding(
                        MicEnabled,
                        Error,
                        name,
                        "Global audio device '"
                            + name
                            + "' ("
                            + slot
                            + ") is enabled and not muted, so OBS captures whichever microphone Windows selects. Set Settings > Audio > Global Audio Devices > Mic/Auxiliary Audio to Disabled."
                    )
            );
        }
    }

    /// <summary>The file path in a path or <c>file:///</c> URL, without its query or fragment.</summary>
    internal static string LocalPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        string text = value.Trim();
        bool fileUrl = text.StartsWith("file:///", StringComparison.OrdinalIgnoreCase);
        if (fileUrl)
        {
            text = text.Substring("file:///".Length);
        }

        int cut = text.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            text = text.Substring(0, cut);
        }

        if (fileUrl)
        {
            text = Uri.UnescapeDataString(text);
        }

        return text.Replace('/', Path.DirectorySeparatorChar);
    }

    private static bool IsUnder(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        return Path.GetFullPath(path)
            .StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Join(IReadOnlyList<string> names) =>
        names.Count == 0 ? "none" : string.Join(", ", names.Take(8));
}
