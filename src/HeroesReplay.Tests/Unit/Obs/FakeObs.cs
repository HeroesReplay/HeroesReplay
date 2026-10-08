using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.Obs.Pages;
using HeroesReplay.Core.Obs.Recording;
using Newtonsoft.Json.Linq;

namespace HeroesReplay.Tests.Unit.Obs;

/// <summary>
/// An obs-websocket stand-in built from a scene collection file. It answers the Get requests
/// the OBS agent tools send and records every request type it receives, Get or not.
/// </summary>
internal sealed class FakeObs : IObsReadSessionFactory
{
    public const string DesktopAudio = "Desktop Audio";
    public const string StreamKey = "live_123456789_FAKE-STREAM-KEY-do-not-print";

    private readonly JObject collection;

    private FakeObs(JObject collection)
    {
        this.collection = collection;
    }

    public List<string> Requests { get; } = new();
    public List<(string Type, JObject Data)> Sent { get; } = new();
    public string Profile { get; set; } = "HeroesReplay";
    public string Collection { get; set; } = "HeroesReplay";
    public string ProgramScene { get; set; } = "game-scene";
    public string Mic { get; set; }
    public bool MicMuted { get; set; }

    /// <summary>
    /// Audio input capture sources in the collection (<c>wasapi_input_capture</c>, not a global
    /// device), by name, with their mute state.
    /// </summary>
    public Dictionary<string, bool> MicSources { get; } = new(StringComparer.Ordinal);

    /// <summary>Inputs whose SetInputMute OBS refuses.</summary>
    public HashSet<string> MuteRefused { get; } = new(StringComparer.Ordinal);

    /// <summary>Runs with the input name on every SetInputMute, before OBS answers it.</summary>
    public Action<string> Muting { get; set; }

    /// <summary>Every SetInputMute this fake received, in order.</summary>
    public List<string> MutedInputs { get; } = new();
    public HashSet<string> MissingRequests { get; } = new(StringComparer.Ordinal);
    public string WebSocketVersion { get; set; } = "5.6.3";
    public bool OmitAvailableRequests { get; set; }
    public Dictionary<string, Exception> Failures { get; } = new(StringComparer.Ordinal);
    public JObject StreamService { get; set; } =
        new()
        {
            ["streamServiceType"] = "rtmp_common",
            ["streamServiceSettings"] = new JObject
            {
                ["service"] = "Twitch",
                ["server"] = "auto",
                ["key"] = StreamKey,
            },
        };
    public byte[] Png { get; set; } = TinyPng.Create(32, 18);

    /// <summary>GetRecordStatus outputActive. StopRecord turns it off unless <see cref="KeepRecordingOnStop"/>.</summary>
    public bool Recording { get; set; } = true;

    /// <summary>GetRecordStatus outputDuration, in milliseconds.</summary>
    public long RecordedMilliseconds { get; set; } = 60000;

    /// <summary>The path StopRecord returns.</summary>
    public string RecordPath { get; set; }

    public bool KeepRecordingOnStop { get; set; }

    /// <summary>StopRecord throws this, like an OBS that refuses the request.</summary>
    public Exception StopRecordError { get; set; }

    /// <summary>GetVideoSettings: a 1920x1080 canvas scaled to 720p at 59.94 FPS.</summary>
    public JObject Video { get; } =
        new()
        {
            ["fpsNumerator"] = 60000,
            ["fpsDenominator"] = 1001,
            ["baseWidth"] = 1920,
            ["baseHeight"] = 1080,
            ["outputWidth"] = 1280,
            ["outputHeight"] = 720,
        };

    /// <summary>GetProfileParameter answers, as the packaged basic.ini sets them.</summary>
    public Dictionary<(string Category, string Name), string> ProfileParameters { get; } =
        new()
        {
            [("Output", "Mode")] = "Simple",
            [("SimpleOutput", "RecFormat2")] = "mp4",
            [("SimpleOutput", "RecEncoder")] = "qsv_h264",
            [("SimpleOutput", "StreamEncoder")] = "x264",
            [("SimpleOutput", "VBitrate")] = "6000",
            [("SimpleOutput", "RecQuality")] = "Stream",
        };

    /// <summary>GetRecordDirectory: where OBS writes its next recording.</summary>
    public string RecordDirectory { get; set; } = @"C:\heroesreplay\Data\Contexts\65820711";
    public int Opened { get; private set; }
    public int Disposed { get; private set; }
    public string OpenedEndpoint { get; private set; }
    public string OpenedPassword { get; private set; }

    public static string RepoObsDirectory()
    {
        string collectionPath = ObsCollectionPaths.FindCollection(AppContext.BaseDirectory);
        return Path.GetDirectoryName(collectionPath)
            ?? throw new InvalidOperationException("obs/Default.json was not found.");
    }

    /// <summary>
    /// The packaged collection as the installer leaves it: paths rewritten to
    /// <paramref name="assetRoot"/> and <paramref name="dataDirectory"/>.
    /// </summary>
    public static FakeObs Installed(string dataDirectory, string assetRoot = null)
    {
        string obs = RepoObsDirectory();
        string json = File.ReadAllText(Path.Combine(obs, "Default.json"));
        return new FakeObs(
            JObject.Parse(ObsCollectionPaths.Rewrite(json, assetRoot ?? obs, dataDirectory))
        );
    }

    /// <summary>The packaged collection with its relative paths, as if never rewritten.</summary>
    public static FakeObs Packaged() =>
        new(JObject.Parse(File.ReadAllText(Path.Combine(RepoObsDirectory(), "Default.json"))));

    public static OBSSettings Settings() =>
        new()
        {
            WebSocketEndpoint = "ws://127.0.0.1:4455",
            WebSocketPassword = string.Empty,
            ProfileName = "HeroesReplay",
            SceneCollectionName = "HeroesReplay",
            GameSceneName = "game-scene",
            WaitingSceneName = "waiting-screen",
            InfoSourceName = "current-replay",
            RankImagesSourceNames = RankImage.SourceNames,
            ReportScenes =
            [
                new ReportScene
                {
                    Enabled = true,
                    SceneName = "match-report",
                    SourceName = "match-report-browser",
                    SourceUrl = new Uri("https://www.heroesprofile.com/Match/Single/[ID]"),
                },
                new ReportScene
                {
                    Enabled = true,
                    SceneName = "prediction-report",
                    SourceName = "prediction-report-browser",
                    SourceUrl = new Uri("file:///C:/heroesreplay/Data/prediction-report.html"),
                },
                new ReportScene
                {
                    Enabled = true,
                    SceneName = "request-queue",
                    SourceName = "request-queue-browser",
                    SourceUrl = new Uri("file:///C:/heroesreplay/Data/queue.html"),
                },
            ],
        };

    public static ObsInspectionSettings InspectionSettings(
        string dataDirectory,
        OBSSettings obs = null,
        bool armed = false
    ) =>
        new(
            obs ?? Settings(),
            AppContext.BaseDirectory,
            dataDirectory,
            armed,
            Path.Combine(dataDirectory, "stream-armed")
        );

    /// <summary>A source or scene entry of the collection, to change before a test reads it.</summary>
    public JObject Source(string name) =>
        Sources().Single(source => (string)source["name"] == name);

    public void RemoveSource(string name)
    {
        Source(name).Remove();
        foreach (JObject scene in Sources().Where(IsScene))
        {
            foreach (
                JToken item in ((JArray)scene["settings"]["items"])
                    .Where(item => (string)item["name"] == name)
                    .ToList()
            )
            {
                item.Remove();
            }
        }
    }

    public void RemoveSceneItem(string scene, string source)
    {
        SceneItem(scene, source).Remove();
    }

    /// <summary>
    /// A scene item as the collection saves it (<c>pos</c>, <c>align</c>, <c>scale</c>,
    /// <c>bounds_type</c>, <c>bounds</c>), to move before a test reads it.
    /// </summary>
    public JObject SceneItem(string scene, string source) =>
        ((JArray)Source(scene)["settings"]["items"])
            .OfType<JObject>()
            .Single(item => (string)item["name"] == source);

    public IObsReadSession Open(string endpoint, string password)
    {
        Opened++;
        OpenedEndpoint = endpoint;
        OpenedPassword = password;
        return new Session(this);
    }

    private IEnumerable<JObject> Sources() => ((JArray)collection["sources"]).OfType<JObject>();

    private static bool IsScene(JObject source) => (string)source["id"] == "scene";

    private JObject Answer(string requestType, JObject data)
    {
        Requests.Add(requestType);
        Sent.Add((requestType, data));
        if (Failures.TryGetValue(requestType, out Exception failure))
        {
            throw failure;
        }

        return requestType switch
        {
            "GetVersion" => new JObject
            {
                ["obsVersion"] = "32.2.2",
                ["obsWebSocketVersion"] = WebSocketVersion,
                ["rpcVersion"] = 1,
                ["platformDescription"] = "Windows 11",
                ["availableRequests"] = OmitAvailableRequests
                    ? null
                    : new JArray(
                        ObsValidator
                            .RequiredRequests.Concat(ObsReadOnly.Requests)
                            .Distinct()
                            .Where(name => !MissingRequests.Contains(name))
                    ),
                ["supportedImageFormats"] = new JArray("png", "jpg"),
            },
            "GetProfileList" => new JObject
            {
                ["currentProfileName"] = Profile,
                ["profiles"] = new JArray(Profile, "Untitled"),
            },
            "GetSceneCollectionList" => new JObject
            {
                ["currentSceneCollectionName"] = Collection,
                ["sceneCollections"] = new JArray(Collection),
            },
            "GetVideoSettings" => (JObject)Video.DeepClone(),
            "GetProfileParameter" => new JObject
            {
                ["parameterValue"] = ProfileParameters.TryGetValue(
                    ((string)data?["parameterCategory"], (string)data?["parameterName"]),
                    out string value
                )
                    ? value
                    : null,
                ["defaultParameterValue"] = null,
            },
            "GetSourceFilterList" => Filters((string)data?["sourceName"]),
            "GetCurrentProgramScene" => new JObject
            {
                ["currentProgramSceneName"] = ProgramScene,
                ["sceneName"] = ProgramScene,
            },
            "GetSceneList" => new JObject
            {
                ["scenes"] = new JArray(
                    Sources()
                        .Where(IsScene)
                        .Select(
                            (scene, index) =>
                                new JObject
                                {
                                    ["sceneIndex"] = index,
                                    ["sceneName"] = scene["name"],
                                }
                        )
                ),
            },
            "GetSceneItemList" => SceneItems((string)data?["sceneName"]),
            "GetSceneItemTransform" => SceneItemTransform(
                (string)data?["sceneName"],
                (long?)data?["sceneItemId"]
            ),
            "GetRecordDirectory" => new JObject { ["recordDirectory"] = RecordDirectory },
            "GetInputList" => new JObject { ["inputs"] = new JArray(Inputs()) },
            "GetInputSettings" => InputSettings((string)data?["inputName"]),
            "GetInputMute" => Audio(
                (string)data?["inputName"],
                muted => new JObject { ["inputMuted"] = muted }
            ),
            "GetInputVolume" => Audio(
                (string)data?["inputName"],
                _ => new JObject { ["inputVolumeMul"] = 1.0, ["inputVolumeDb"] = 0.0 }
            ),
            "GetSpecialInputs" => new JObject
            {
                ["desktop1"] = DesktopAudio,
                ["desktop2"] = null,
                ["mic1"] = Mic,
                ["mic2"] = null,
                ["mic3"] = null,
                ["mic4"] = null,
            },
            "GetStreamStatus" => new JObject
            {
                ["outputActive"] = false,
                ["outputReconnecting"] = false,
                ["outputTimecode"] = "00:00:00.000",
                ["outputDuration"] = 0,
                ["outputCongestion"] = 0,
                ["outputBytes"] = 0,
                ["outputSkippedFrames"] = 3,
                ["outputTotalFrames"] = 1000,
            },
            "GetRecordStatus" => new JObject
            {
                ["outputActive"] = Recording,
                ["outputPaused"] = false,
                ["outputTimecode"] = "00:01:00.000",
                ["outputDuration"] = RecordedMilliseconds,
                ["outputBytes"] = 1234567,
            },
            "GetStats" => new JObject
            {
                ["cpuUsage"] = 12.3456,
                ["memoryUsage"] = 512.5,
                ["availableDiskSpace"] = 100000.25,
                ["activeFps"] = 59.94,
                ["averageFrameRenderTime"] = 1.234,
                ["renderSkippedFrames"] = 1,
                ["renderTotalFrames"] = 5000,
                ["outputSkippedFrames"] = 2,
                ["outputTotalFrames"] = 4000,
            },
            "GetStreamServiceSettings" => (JObject)StreamService.DeepClone(),
            "GetSourceScreenshot" => Screenshot((string)data?["sourceName"]),
            _ => throw new ObsRequestException(requestType, 204, "Unknown request type."),
        };
    }

    private JObject SceneItems(string sceneName)
    {
        JObject scene = Sources()
            .FirstOrDefault(source => IsScene(source) && (string)source["name"] == sceneName);
        if (scene == null)
        {
            throw new ObsRequestException("GetSceneItemList", 600, "No source was found.");
        }

        var byName = Sources().ToDictionary(source => (string)source["name"]);
        return new JObject
        {
            ["sceneItems"] = new JArray(
                ((JArray)scene["settings"]["items"]).Select(
                    (item, index) =>
                    {
                        byName.TryGetValue((string)item["name"], out JObject source);
                        bool nested = source != null && IsScene(source);
                        return new JObject
                        {
                            ["sceneItemId"] = item["id"],
                            ["sceneItemIndex"] = index,
                            ["sourceName"] = item["name"],
                            ["sourceType"] = nested
                                ? "OBS_SOURCE_TYPE_SCENE"
                                : "OBS_SOURCE_TYPE_INPUT",
                            ["inputKind"] = nested ? null : VersionedKind(source),
                            ["sceneItemEnabled"] = item["visible"],
                            ["isGroup"] = false,
                        };
                    }
                )
            ),
        };
    }

    private static readonly string[] BoundsTypes =
    {
        "OBS_BOUNDS_NONE",
        "OBS_BOUNDS_STRETCH",
        "OBS_BOUNDS_SCALE_INNER",
        "OBS_BOUNDS_SCALE_OUTER",
        "OBS_BOUNDS_SCALE_TO_WIDTH",
        "OBS_BOUNDS_SCALE_TO_HEIGHT",
        "OBS_BOUNDS_MAX_ONLY",
    };

    /// <summary>GetSceneItemTransform from the item's saved transform, as obs-websocket names it.</summary>
    private JObject SceneItemTransform(string sceneName, long? sceneItemId)
    {
        JObject item = Sources()
            .Where(source => IsScene(source) && (string)source["name"] == sceneName)
            .SelectMany(scene => ((JArray)scene["settings"]["items"]).OfType<JObject>())
            .FirstOrDefault(candidate => (long?)candidate["id"] == sceneItemId);
        if (item == null)
        {
            throw new ObsRequestException("GetSceneItemTransform", 600, "No scene item was found.");
        }

        return new JObject
        {
            ["sceneItemTransform"] = new JObject
            {
                ["positionX"] = item["pos"]?["x"] ?? 0,
                ["positionY"] = item["pos"]?["y"] ?? 0,
                ["rotation"] = item["rot"] ?? 0,
                ["scaleX"] = item["scale"]?["x"] ?? 1,
                ["scaleY"] = item["scale"]?["y"] ?? 1,
                ["alignment"] = item["align"] ?? 5,
                ["boundsType"] = BoundsTypes[(int?)item["bounds_type"] ?? 0],
                ["boundsAlignment"] = item["bounds_align"] ?? 0,
                ["boundsWidth"] = item["bounds"]?["x"] ?? 0,
                ["boundsHeight"] = item["bounds"]?["y"] ?? 0,
                ["cropLeft"] = item["crop_left"] ?? 0,
                ["cropRight"] = item["crop_right"] ?? 0,
                ["cropTop"] = item["crop_top"] ?? 0,
                ["cropBottom"] = item["crop_bottom"] ?? 0,
                ["sourceWidth"] = 1920,
                ["sourceHeight"] = 1080,
            },
        };
    }

    private IEnumerable<JObject> Inputs()
    {
        foreach (JObject source in Sources().Where(source => !IsScene(source)))
        {
            yield return new JObject
            {
                ["inputName"] = source["name"],
                ["inputKind"] = VersionedKind(source),
                ["unversionedInputKind"] = source["id"],
            };
        }

        yield return new JObject
        {
            ["inputName"] = DesktopAudio,
            ["inputKind"] = "wasapi_output_capture",
            ["unversionedInputKind"] = "wasapi_output_capture",
        };
        if (Mic != null)
        {
            yield return new JObject
            {
                ["inputName"] = Mic,
                ["inputKind"] = "wasapi_input_capture",
                ["unversionedInputKind"] = "wasapi_input_capture",
            };
        }

        foreach (string source in MicSources.Keys)
        {
            yield return new JObject
            {
                ["inputName"] = source,
                ["inputKind"] = "wasapi_input_capture",
                ["unversionedInputKind"] = "wasapi_input_capture",
            };
        }
    }

    private static string VersionedKind(JObject source) =>
        (string)source?["versioned_id"] ?? (string)source?["id"];

    private JObject InputSettings(string inputName)
    {
        JObject source = Sources()
            .FirstOrDefault(candidate =>
                !IsScene(candidate) && (string)candidate["name"] == inputName
            );
        if (source == null)
        {
            if (
                inputName == DesktopAudio
                || (inputName != null && (inputName == Mic || MicSources.ContainsKey(inputName)))
            )
            {
                return new JObject
                {
                    ["inputSettings"] = new JObject { ["device_id"] = "default" },
                };
            }

            throw new ObsRequestException("GetInputSettings", 600, "No source was found.");
        }

        return new JObject
        {
            ["inputSettings"] = source["settings"]?.DeepClone() ?? new JObject(),
            ["inputKind"] = VersionedKind(source),
        };
    }

    private JObject Audio(string inputName, Func<bool, JObject> answer)
    {
        if (inputName == DesktopAudio)
        {
            return answer(false);
        }

        if (inputName != null && inputName == Mic)
        {
            return answer(MicMuted);
        }

        if (inputName != null && MicSources.TryGetValue(inputName, out bool muted))
        {
            return answer(muted);
        }

        throw new ObsRequestException(
            "GetInputMute",
            604,
            "The specified input does not support audio."
        );
    }

    /// <summary>GetSourceFilterList from the source's <c>filters</c> in the collection.</summary>
    private JObject Filters(string sourceName)
    {
        JObject source = Sources()
            .FirstOrDefault(candidate => (string)candidate["name"] == sourceName);
        if (source == null)
        {
            throw new ObsRequestException("GetSourceFilterList", 600, "No source was found.");
        }

        return new JObject
        {
            ["filters"] = new JArray(
                (source["filters"] as JArray ?? new JArray())
                    .OfType<JObject>()
                    .Select(
                        (filter, index) =>
                            new JObject
                            {
                                ["filterName"] = filter["name"],
                                ["filterKind"] = filter["id"],
                                ["filterEnabled"] = filter["enabled"] ?? true,
                                ["filterIndex"] = index,
                            }
                    )
            ),
        };
    }

    private JObject Screenshot(string sourceName)
    {
        if (!Sources().Any(source => (string)source["name"] == sourceName))
        {
            throw new ObsRequestException("GetSourceScreenshot", 600, "No source was found.");
        }

        return new JObject
        {
            ["imageData"] = "data:image/png;base64," + Convert.ToBase64String(Png),
        };
    }

    /// <summary>A session that may also reload browser sources, as <c>obs pages</c> opens.</summary>
    public IObsPageSession OpenPage()
    {
        Opened++;
        return new Session(this);
    }

    /// <summary>The browser sources reloaded through <see cref="IObsPageSession.Reload"/>, in order.</summary>
    public List<string> Reloaded { get; } = new();

    /// <summary>A session that may also stop the recording, as <c>services stop</c> opens.</summary>
    public IObsRecordStopSession OpenRecordStop()
    {
        Opened++;
        return new Session(this);
    }

    /// <summary>A session that may also mute an input, as the spectator's microphone mute uses.</summary>
    public IObsMicrophoneSession OpenMicrophones()
    {
        Opened++;
        return new Session(this);
    }

    /// <summary>
    /// SetInputMute to muted. Desktop Audio and the collection's sources answer as OBS would
    /// (muted), so a mute sent to the wrong input is in <see cref="MutedInputs"/> for a test to see.
    /// </summary>
    private void Mute(string inputName)
    {
        Requests.Add("SetInputMute");
        Sent.Add(
            ("SetInputMute", new JObject { ["inputName"] = inputName, ["inputMuted"] = true })
        );
        MutedInputs.Add(inputName);
        Muting?.Invoke(inputName);
        if (Failures.TryGetValue("SetInputMute", out Exception failure))
        {
            throw failure;
        }

        if (inputName != null && MuteRefused.Contains(inputName))
        {
            throw new ObsRequestException("SetInputMute", 604, "The input refused the mute.");
        }

        if (inputName != null && inputName == Mic)
        {
            MicMuted = true;
        }
        else if (inputName != null && MicSources.ContainsKey(inputName))
        {
            MicSources[inputName] = true;
        }
        else if (
            inputName != DesktopAudio
            && !Sources().Any(source => (string)source["name"] == inputName)
        )
        {
            throw new ObsRequestException("SetInputMute", 600, "No source was found.");
        }
    }

    private string StopRecord()
    {
        Requests.Add("StopRecord");
        if (StopRecordError != null)
        {
            throw StopRecordError;
        }

        if (!KeepRecordingOnStop)
        {
            Recording = false;
        }

        return RecordPath;
    }

    private sealed class Session : IObsPageSession, IObsRecordStopSession, IObsMicrophoneSession
    {
        private readonly FakeObs owner;

        public Session(FakeObs owner)
        {
            this.owner = owner;
        }

        public JObject Get(string requestType, JObject requestData = null) =>
            owner.Answer(requestType, requestData);

        public void Reload(string inputName)
        {
            owner.Requests.Add("PressInputPropertiesButton");
            owner.Reloaded.Add(inputName);
        }

        public string StopRecord() => owner.StopRecord();

        public void Mute(string inputName) => owner.Mute(inputName);

        public void Dispose() => owner.Disposed++;
    }
}
