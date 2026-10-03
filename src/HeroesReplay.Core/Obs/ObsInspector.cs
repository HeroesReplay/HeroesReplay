using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// The live OBS state an agent needs to judge a machine, read through
/// <see cref="IObsReadSession"/>. <see cref="Ok"/> is false only when OBS could not be read
/// at all; a request that failed is named in <see cref="Unread"/>.
/// </summary>
public sealed record ObsInspection
{
    public int SchemaVersion => 1;
    public bool Ok { get; init; }

    /// <summary>Stable failure code, such as <c>obs.unreachable</c>. Null when <see cref="Ok"/>.</summary>
    public string Code { get; init; }
    public string Error { get; init; }
    public string Endpoint { get; init; }
    public ObsVersionInfo Version { get; init; }
    public ObsSelectionInfo Selection { get; init; }
    public ObsVideoInfo Video { get; init; }

    /// <summary>Output mode, recording format, and encoders from the active profile.</summary>
    public ObsProfileInfo Profile { get; init; }
    public string ProgramScene { get; init; }
    public IReadOnlyList<ObsSceneInfo> Scenes { get; init; }
    public IReadOnlyList<ObsInputInfo> Inputs { get; init; }
    public ObsStreamStatusInfo Stream { get; init; }
    public ObsRecordStatusInfo Record { get; init; }
    public ObsStatsInfo Stats { get; init; }

    /// <summary>The service type and whether a key is set. Never the key or the server.</summary>
    public ObsStreamService StreamService { get; init; }
    public ObsStreamArmInfo StreamArm { get; init; }
    public IReadOnlyList<string> Unread { get; init; } = [];
}

public sealed record ObsVersionInfo(
    string ObsVersion,
    string WebsocketVersion,
    long? RpcVersion,
    string Platform,
    int AvailableRequestCount
);

public sealed record ObsSelectionInfo(
    string ExpectedProfile,
    string ActiveProfile,
    bool ProfileMatches,
    string ExpectedCollection,
    string ActiveCollection,
    bool CollectionMatches,
    bool Ok,
    string Reason,
    string Detail,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<string> Collections
);

public sealed record ObsVideoInfo(
    long? BaseWidth,
    long? BaseHeight,
    long? OutputWidth,
    long? OutputHeight,
    long? FpsNumerator,
    long? FpsDenominator,
    double? Fps
);

public sealed record ObsSceneInfo(string Name, IReadOnlyList<ObsSceneItemInfo> Items);

/// <summary><see cref="Kind"/> is the input kind, or <c>scene</c> / <c>group</c> for a nested scene.</summary>
public sealed record ObsSceneItemInfo(string Name, string Kind, bool? Enabled, long? Id);

/// <summary>
/// <see cref="GlobalAudio"/> is the OBS global audio slot (<c>desktop1</c>, <c>desktop2</c>,
/// <c>mic1</c>…<c>mic4</c>) from GetSpecialInputs, or null for a source in a scene. Mute and
/// volume are null for an input without audio.
/// </summary>
public sealed record ObsInputInfo(
    string Name,
    string Kind,
    string UnversionedKind,
    string GlobalAudio,
    bool? Muted,
    double? VolumeMul,
    double? VolumeDb
);

/// <summary><see cref="DroppedFrames"/> are frames the stream output dropped (network).</summary>
public sealed record ObsStreamStatusInfo(
    bool? Active,
    bool? Reconnecting,
    string Timecode,
    long? DurationMs,
    double? Congestion,
    long? Bytes,
    long? DroppedFrames,
    long? TotalFrames
);

public sealed record ObsRecordStatusInfo(
    bool? Active,
    bool? Paused,
    string Timecode,
    long? DurationMs,
    long? Bytes
);

/// <summary>
/// <see cref="RenderSkippedFrames"/> are frames missed because rendering lagged;
/// <see cref="OutputSkippedFrames"/> are frames skipped because encoding lagged.
/// </summary>
public sealed record ObsStatsInfo(
    double? CpuUsagePercent,
    double? MemoryUsageMb,
    double? AvailableDiskSpaceMb,
    double? ActiveFps,
    double? AverageFrameRenderTimeMs,
    long? RenderSkippedFrames,
    long? RenderTotalFrames,
    long? OutputSkippedFrames,
    long? OutputTotalFrames
);

/// <summary>
/// <see cref="MayStart"/> means both keys are present: <c>OBS:StreamingEnabled</c> and the arm.
/// <see cref="BlockedBy"/> is <c>obs.stream_not_armed</c> when the settings want a stream this
/// machine may not start.
/// </summary>
public sealed record ObsStreamArmInfo(
    bool Armed,
    string ArmFile,
    bool StreamingEnabled,
    bool MayStart,
    string BlockedBy
)
{
    public static ObsStreamArmInfo From(ObsInspectionSettings settings)
    {
        bool streaming = SessionMedia.ShouldStream(settings?.Obs);
        bool armed = settings?.Armed == true;
        return new ObsStreamArmInfo(
            armed,
            settings?.ArmFile,
            streaming,
            streaming && armed,
            TwitchIngestGuard.BlockedBy(streaming, armed)
        );
    }
}

public static class ObsInspector
{
    private static readonly string[] SpecialInputSlots =
    {
        "desktop1",
        "desktop2",
        "mic1",
        "mic2",
        "mic3",
        "mic4",
    };

    public static ObsInspection Unavailable(
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
            StreamArm = ObsStreamArmInfo.From(settings),
        };

    public static ObsInspection Inspect(IObsReadSession session, ObsInspectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(session);
        var unread = new List<string>();
        JObject Read(string requestType, JObject requestData = null)
        {
            try
            {
                return session.Get(requestType, requestData);
            }
            catch (ObsRequestException e)
            {
                unread.Add(e.Message);
                return null;
            }
        }

        JObject version = Read("GetVersion");
        JObject program = Read("GetCurrentProgramScene");
        return new ObsInspection
        {
            Ok = true,
            Endpoint = settings?.Obs?.WebSocketEndpoint,
            Version =
                version == null
                    ? null
                    : new ObsVersionInfo(
                        ObsResponse.String(version, "obsVersion"),
                        ObsResponse.String(version, "obsWebSocketVersion"),
                        ObsResponse.Long(version, "rpcVersion"),
                        ObsResponse.String(version, "platformDescription"),
                        ObsResponse.Strings(version, "availableRequests").Count
                    ),
            Selection = ReadSelection(
                settings?.Obs,
                Read("GetProfileList"),
                Read("GetSceneCollectionList")
            ),
            Video = ReadVideo(Read("GetVideoSettings")),
            Profile = ReadProfile(session, unread),
            ProgramScene = ProgramSceneName(program),
            Scenes = ReadScenes(Read("GetSceneList"), Read),
            Inputs = ReadInputs(session, Read("GetInputList"), Read("GetSpecialInputs")),
            Stream = ReadStream(Read("GetStreamStatus")),
            Record = ReadRecord(Read("GetRecordStatus")),
            Stats = ReadStats(Read("GetStats")),
            StreamService = ReadStreamService(Read("GetStreamServiceSettings")),
            StreamArm = ObsStreamArmInfo.From(settings),
            Unread = unread,
        };
    }

    private static ObsProfileInfo ReadProfile(IObsReadSession session, List<string> unread)
    {
        try
        {
            return ObsProfileInfo.Read(session);
        }
        catch (ObsRequestException e)
        {
            unread.Add(e.Message);
            return null;
        }
    }

    internal static string ProgramSceneName(JObject program) =>
        ObsResponse.String(program, "currentProgramSceneName")
        ?? ObsResponse.String(program, "sceneName");

    /// <summary>GetSpecialInputs: input name → slot (<c>desktop1</c>, <c>mic1</c>, …).</summary>
    internal static IReadOnlyDictionary<string, string> GlobalAudio(JObject special)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string slot in SpecialInputSlots)
        {
            string name = ObsResponse.String(special, slot);
            if (!string.IsNullOrWhiteSpace(name))
            {
                slots.TryAdd(name, slot);
            }
        }

        return slots;
    }

    /// <summary>Null when the input has no audio: OBS fails GetInputMute for it.</summary>
    internal static bool? ReadMuted(IObsReadSession session, string inputName)
    {
        try
        {
            return ObsResponse.Bool(
                session.Get("GetInputMute", new JObject { ["inputName"] = inputName }),
                "inputMuted"
            );
        }
        catch (ObsRequestException)
        {
            return null;
        }
    }

    private static ObsStreamService ReadStreamService(JObject response) =>
        // Only the type and whether a key is set leave this method.
        response == null
            ? null
            : ObsStreamService.Summarize(response);

    private static ObsSelectionInfo ReadSelection(
        OBSSettings obs,
        JObject profiles,
        JObject collections
    )
    {
        string expectedProfile = ObsNames.Profile(obs);
        string expectedCollection = ObsNames.SceneCollection(obs);
        string activeProfile = ObsResponse.String(profiles, "currentProfileName");
        string activeCollection = ObsResponse.String(collections, "currentSceneCollectionName");
        ObsSelectionResult check = ObsSelection.Check(
            expectedProfile,
            expectedCollection,
            activeProfile,
            activeCollection
        );
        return new ObsSelectionInfo(
            expectedProfile,
            activeProfile,
            string.Equals(activeProfile, expectedProfile, StringComparison.Ordinal),
            expectedCollection,
            activeCollection,
            string.Equals(activeCollection, expectedCollection, StringComparison.Ordinal),
            check.Ok,
            check.Reason,
            check.Detail,
            ObsResponse.Strings(profiles, "profiles"),
            ObsResponse.Strings(collections, "sceneCollections")
        );
    }

    private static ObsVideoInfo ReadVideo(JObject video)
    {
        if (video == null)
        {
            return null;
        }

        long? numerator = ObsResponse.Long(video, "fpsNumerator");
        long? denominator = ObsResponse.Long(video, "fpsDenominator");
        return new ObsVideoInfo(
            ObsResponse.Long(video, "baseWidth"),
            ObsResponse.Long(video, "baseHeight"),
            ObsResponse.Long(video, "outputWidth"),
            ObsResponse.Long(video, "outputHeight"),
            numerator,
            denominator,
            numerator.HasValue && denominator is > 0
                ? Math.Round((double)numerator.Value / denominator.Value, 3)
                : null
        );
    }

    private static IReadOnlyList<ObsSceneInfo> ReadScenes(
        JObject sceneList,
        Func<string, JObject, JObject> read
    )
    {
        if (sceneList == null)
        {
            return null;
        }

        // OBS lists index 0 at the bottom of the Scenes dock; show the dock's order.
        return ObsResponse
            .Objects(sceneList, "scenes")
            .OrderByDescending(scene => ObsResponse.Long(scene, "sceneIndex") ?? 0)
            .Select(scene => ObsResponse.String(scene, "sceneName"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => new ObsSceneInfo(
                name,
                SceneItems(read("GetSceneItemList", new JObject { ["sceneName"] = name }))
            ))
            .ToList();
    }

    internal static IReadOnlyList<ObsSceneItemInfo> SceneItems(JObject itemList)
    {
        if (itemList == null)
        {
            return null;
        }

        return ObsResponse
            .Objects(itemList, "sceneItems")
            .OrderByDescending(item => ObsResponse.Long(item, "sceneItemIndex") ?? 0)
            .Select(item => new ObsSceneItemInfo(
                ObsResponse.String(item, "sourceName"),
                ItemKind(item),
                ObsResponse.Bool(item, "sceneItemEnabled"),
                ObsResponse.Long(item, "sceneItemId")
            ))
            .ToList();
    }

    private static string ItemKind(JObject item)
    {
        string kind = ObsResponse.String(item, "inputKind");
        if (!string.IsNullOrWhiteSpace(kind))
        {
            return kind;
        }

        if (ObsResponse.Bool(item, "isGroup") == true)
        {
            return "group";
        }

        string type = ObsResponse.String(item, "sourceType");
        return string.Equals(type, "OBS_SOURCE_TYPE_SCENE", StringComparison.Ordinal)
            ? "scene"
            : type;
    }

    private static IReadOnlyList<ObsInputInfo> ReadInputs(
        IObsReadSession session,
        JObject inputList,
        JObject special
    )
    {
        if (inputList == null)
        {
            return null;
        }

        IReadOnlyDictionary<string, string> global = GlobalAudio(special);
        var inputs = new List<ObsInputInfo>();
        foreach (JObject input in ObsResponse.Objects(inputList, "inputs"))
        {
            string name = ObsResponse.String(input, "inputName");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            bool? muted = ReadMuted(session, name);
            JObject volume = null;
            if (muted.HasValue)
            {
                try
                {
                    volume = session.Get("GetInputVolume", new JObject { ["inputName"] = name });
                }
                catch (ObsRequestException)
                {
                    // Mute and volume answer for the same inputs; keep the mute state.
                }
            }

            inputs.Add(
                new ObsInputInfo(
                    name,
                    ObsResponse.String(input, "inputKind"),
                    ObsResponse.String(input, "unversionedInputKind"),
                    global.TryGetValue(name, out string slot) ? slot : null,
                    muted,
                    ObsResponse.Double(volume, "inputVolumeMul"),
                    ObsResponse.Double(volume, "inputVolumeDb")
                )
            );
        }

        return inputs;
    }

    private static ObsStreamStatusInfo ReadStream(JObject status) =>
        status == null
            ? null
            : new ObsStreamStatusInfo(
                ObsResponse.Bool(status, "outputActive"),
                ObsResponse.Bool(status, "outputReconnecting"),
                ObsResponse.String(status, "outputTimecode"),
                ObsResponse.Long(status, "outputDuration"),
                ObsResponse.Double(status, "outputCongestion"),
                ObsResponse.Long(status, "outputBytes"),
                ObsResponse.Long(status, "outputSkippedFrames"),
                ObsResponse.Long(status, "outputTotalFrames")
            );

    private static ObsRecordStatusInfo ReadRecord(JObject status) =>
        status == null
            ? null
            : new ObsRecordStatusInfo(
                ObsResponse.Bool(status, "outputActive"),
                ObsResponse.Bool(status, "outputPaused"),
                ObsResponse.String(status, "outputTimecode"),
                ObsResponse.Long(status, "outputDuration"),
                ObsResponse.Long(status, "outputBytes")
            );

    private static ObsStatsInfo ReadStats(JObject stats) =>
        stats == null
            ? null
            : new ObsStatsInfo(
                Round(ObsResponse.Double(stats, "cpuUsage")),
                Round(ObsResponse.Double(stats, "memoryUsage")),
                Round(ObsResponse.Double(stats, "availableDiskSpace")),
                Round(ObsResponse.Double(stats, "activeFps")),
                Round(ObsResponse.Double(stats, "averageFrameRenderTime")),
                ObsResponse.Long(stats, "renderSkippedFrames"),
                ObsResponse.Long(stats, "renderTotalFrames"),
                ObsResponse.Long(stats, "outputSkippedFrames"),
                ObsResponse.Long(stats, "outputTotalFrames")
            );

    private static double? Round(double? value) =>
        value.HasValue ? Math.Round(value.Value, 2) : null;
}
