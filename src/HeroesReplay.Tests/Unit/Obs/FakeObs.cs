using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Obs;
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
    public HashSet<string> MissingRequests { get; } = new(StringComparer.Ordinal);
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
        };
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
        ((JArray)Source(scene)["settings"]["items"])
            .Single(item => (string)item["name"] == source)
            .Remove();
    }

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
                ["obsWebSocketVersion"] = "5.6.3",
                ["rpcVersion"] = 1,
                ["platformDescription"] = "Windows 11",
                ["availableRequests"] = new JArray(
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
                ["outputActive"] = true,
                ["outputPaused"] = false,
                ["outputTimecode"] = "00:01:00.000",
                ["outputDuration"] = 60000,
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
            if (inputName == DesktopAudio || (inputName != null && inputName == Mic))
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

    private sealed class Session : IObsPageSession
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

        public void Dispose() => owner.Disposed++;
    }
}

internal static class TinyPng
{
    /// <summary>A noisy PNG, so its base64 is far longer than 100 characters.</summary>
    public static byte[] Create(int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        var random = new Random(7);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                bitmap.SetPixel(
                    x,
                    y,
                    Color.FromArgb(random.Next(256), random.Next(256), random.Next(256))
                );
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
