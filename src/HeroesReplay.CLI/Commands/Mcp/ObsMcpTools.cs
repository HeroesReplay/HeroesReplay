using System;
using System.ComponentModel;
using HeroesReplay.Core.Obs.Inspection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HeroesReplay.CLI.Commands.Mcp;

/// <summary>
/// Read-only OBS tools for agents. Each call opens its own short obs-websocket session,
/// sends only the Get requests in <see cref="ObsReadOnly"/>, and disconnects. The spectator's
/// session is not touched. Nothing here selects a scene, changes a source, or starts or stops
/// a stream or recording; fixes go through guarded CLI commands such as <c>obs arm</c>.
/// </summary>
[McpServerToolType]
public sealed class ObsMcpTools
{
    public const string PasswordUnresolved = ObsLiveRead.PasswordUnresolved;
    public const string SettingsUnreadable = ObsLiveRead.SettingsUnreadable;
    public const string RequestFailed = ObsLiveRead.RequestFailed;
    public const string SourceNotFound = ObsLiveRead.SourceNotFound;

    private readonly IObsReadSessionFactory sessions;
    private readonly Func<ObsInspectionSettings> settings;

    public ObsMcpTools(IObsReadSessionFactory sessions, Func<ObsInspectionSettings> settings)
    {
        this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    [
        McpServerTool(
            Name = "obs_inspect",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            UseStructuredContent = true
        ),
        Description(
            "Read live OBS over obs-websocket without changing it: OBS and websocket versions, active profile and scene collection against OBS:ProfileName / OBS:SceneCollectionName, video settings, the profile's output mode, encoders and bitrates, the record directory, program scene, scenes and their items, inputs with mute and volume (global audio devices flagged), stream and record status, GetStats, the stream service type and whether a key is set (never the key), and this machine's stream arm."
        )
    ]
    public ObsInspection Inspect()
    {
        ObsLiveReadResult<ObsInspection> run = Run(ObsInspector.Inspect);
        return run.Value ?? ObsInspector.Unavailable(run.Settings, run.Code, run.Message);
    }

    [
        McpServerTool(
            Name = "obs_validate",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false,
            UseStructuredContent = true
        ),
        Description(
            "Compare the collection OBS has loaded with the packaged obs/Default.json contract, and the install's OBS files with obs/bundle.manifest, without changing anything. Findings have stable codes: obs.profile_mismatch, obs.collection_mismatch, obs.scene_missing, obs.source_missing, obs.source_kind_mismatch, obs.scene_item_missing, obs.scene_item_misplaced, obs.file_missing, obs.file_unverifiable, obs.runtime_file_missing, obs.path_stale, obs.url_invalid, obs.mic_enabled, obs.mic_muted, obs.collection_custom, obs.request_unavailable, obs.asset_missing, obs.bundle_invalid, obs.bundle_unverified, obs.canvas_mismatch, obs.fps_low, obs.profile_unreadable, obs.recording_format, obs.recording_format_invalid, obs.recording_not_crash_safe, obs.bitrate_low, obs.stream_key_missing, obs.stream_service_unexpected, obs.filter_missing, obs.filter_stale. ok is false when any finding is an error."
        )
    ]
    public ObsValidation Validate()
    {
        ObsLiveReadResult<ObsValidation> run = Run(ObsValidator.Validate);
        return run.Value ?? ObsValidator.Unavailable(run.Settings, run.Code, run.Message);
    }

    [
        McpServerTool(
            Name = "obs_screenshot",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        ),
        Description(
            "PNG screenshot of the current OBS program scene, or of a named scene or source, returned as an MCP image. Does not change the scene."
        )
    ]
    public CallToolResult Screenshot(
        [Description("Scene or source name. Omit for the current program scene.")]
            string source = null,
        [Description(
            "Image width in pixels (default 960, at most 1920). The height keeps the aspect ratio."
        )]
            int width = ObsScreenshot.DefaultWidth
    )
    {
        ObsLiveReadResult<ObsScreenshot> run = Run(
            (session, _) => ObsScreenshot.Capture(session, source, width)
        );
        if (run.Value == null)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = run.Code + ": " + run.Message }],
            };
        }

        ObsScreenshot shot = run.Value;
        return new CallToolResult
        {
            Content =
            [
                ImageContentBlock.FromBytes(shot.Png, "image/png"),
                new TextContentBlock
                {
                    Text =
                        (shot.ProgramScene ? "Program scene '" : "Source '")
                        + shot.Source
                        + "', "
                        + shot.Width
                        + "x"
                        + shot.Height
                        + " PNG, "
                        + shot.Png.Length
                        + " bytes.",
                },
            ],
        };
    }

    private ObsLiveReadResult<T> Run<T>(Func<IObsReadSession, ObsInspectionSettings, T> read)
        where T : class => ObsLiveRead.Run(sessions, settings, read);
}
