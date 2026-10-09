using System;
using System.Collections.Generic;
using HeroesReplay.Core.Obs.Recording;

namespace HeroesReplay.Core.Obs;

public class OBSSettings
{
    /// <summary>
    /// When false, spectate sends OBS nothing: no scenes, no report scenes, and no recording,
    /// whatever <see cref="RecordingEnabled"/> and <see cref="RecordRequestedReplays"/> say (#318).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Optional path to obs64.exe. Empty = default Program Files install.</summary>
    public string ExecutablePath { get; set; }

    /// <summary>Record every spectated replay. Applies only while <see cref="Enabled"/> is true.</summary>
    public bool RecordingEnabled { get; set; }

    /// <summary>
    /// The recording container. Right before each StartRecord the spectator sets the active
    /// output mode's <c>RecFormat2</c> (<c>SimpleOutput</c> or <c>AdvOut</c>) to it, so the
    /// machine's profile does not decide it (#310). One of <c>mp4</c>, <c>hybrid_mp4</c>,
    /// <c>fragmented_mp4</c> (the default: the only one that stayed usable when OBS and its muxer
    /// were killed), or <c>mkv</c> (not found by the uploader, clips, or retention, which look for
    /// <c>*.mp4</c>). Any other value is <c>obs.recording_format_invalid</c>, and the profile's
    /// format is left as it is.
    /// </summary>
    public string RecordingFormat { get; set; } = ObsRecordingFormat.Default;

    /// <summary>
    /// When false (the default), HeroesReplay never calls OBS StartStream/StopStream.
    /// Dev VMs must leave this off so connectivity recovery cannot go live. A stream also
    /// needs this machine's arm (<see cref="ObsStreamArm"/>), which no settings file can set.
    /// </summary>
    public bool StreamingEnabled { get; set; }

    /// <summary>
    /// How long a desired stream may stay reconnecting, or active with frozen bytes, before the
    /// spectator stops it and starts it again (#395). OBS's own reconnect owns a drop shorter than
    /// this. Default 90 seconds.
    /// </summary>
    public TimeSpan StreamStuckAfter { get; set; } = ObsStreamRecovery.DefaultStuckAfter;

    /// <summary>
    /// OBS profile that must be active before HeroesReplay starts a stream or a recording.
    /// The profile folder under <c>%APPDATA%\obs-studio\basic\profiles</c> has this name.
    /// </summary>
    public string ProfileName { get; set; } = ObsNames.Default;

    /// <summary>
    /// OBS scene collection that must be active. The live file is
    /// <c>%APPDATA%\obs-studio\basic\scenes\{SceneCollectionName}.json</c>.
    /// </summary>
    public string SceneCollectionName { get; set; } = ObsNames.Default;

    /// <summary>
    /// When true (the default), a new collection template reaches OBS while it runs, without
    /// stopping the stream: between replays OBS switches to the spare collection
    /// (<c>{SceneCollectionName}-next</c>) holding the new layout, the main file is rewritten, and
    /// OBS switches back. When false, the collection is replaced only while OBS is closed.
    /// </summary>
    public bool LiveCollectionSwap { get; set; } = true;

    /// <summary>
    /// When true, the managed collection points at a verified copy of this install's OBS files
    /// under <c>%LOCALAPPDATA%\HeroesReplay\obs\assets\&lt;bundle-hash&gt;</c>, not at the
    /// install's own <c>obs</c> folder, so removing a build's folder (a git worktree) leaves no
    /// missing images (#330). On in <c>appsettings.dev.json</c>. Off by default and in prod: a
    /// release updates <c>app\obs</c> in place, and OBS reloads a changed asset from there.
    /// </summary>
    public bool StableAssets { get; set; }

    /// <summary>
    /// When true, shutdown may close an OBS process this coordinator launched.
    /// A process that was already running is never closed. Default false.
    /// </summary>
    public bool CloseOwnedOnStop { get; set; }

    /// <summary>
    /// When true (the default), spectate mutes every microphone input OBS has, the global
    /// Mic/Aux devices and any audio input capture source, at each replay's session start and
    /// right before it starts the stream (#314). Desktop Audio, media, and browser sources are
    /// never touched. A mute that fails is a warning; it never stops the stream.
    /// </summary>
    public bool MuteMicrophones { get; set; } = true;

    /// <summary>
    /// After HeroesReplay starts OBS itself, how long the websocket identify is retried before
    /// it fails. An OBS that was already running gets one attempt. Default 60 seconds.
    /// </summary>
    public TimeSpan StartupIdentifyTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The supervisor's OBS watchdog (#398, <c>ServiceRestart:ObsWatchdog</c>) treats a running
    /// OBS as hung when its websocket has not identified or answered for this long and its stream
    /// is not live (the bytes do not advance, or the status cannot be read). Default 3 minutes.
    /// </summary>
    public TimeSpan HungAfter { get; set; } = TimeSpan.FromMinutes(3);

    public bool RecordRequestedReplays { get; set; }
    public string InfoFileName { get; set; }
    public string WebSocketEndpoint { get; set; }
    public string WebSocketPassword { get; set; }
    public string GameSceneName { get; set; }
    public string WaitingSceneName { get; set; }

    /// <summary>
    /// Minimum wait after the previous game exits, before the next replay launches.
    /// Report scenes finish in this time. When they finish first, the waiting scene
    /// stays up for the rest. Zero launches immediately. Default 90 seconds.
    /// </summary>
    public TimeSpan BeforeNextReplay { get; set; } = TimeSpan.FromSeconds(90);
    public IEnumerable<ReportScene> ReportScenes { get; set; }
    public string ReportBrowserCss { get; set; }

    /// <summary>
    /// When true, the match-report browser source hides the Heroes Profile site menu and the event banner above the match.
    /// </summary>
    public bool HideReportHeader { get; set; } = true;
    public IEnumerable<string> RankImagesSourceNames { get; set; }

    public string InfoSourceName { get; set; }

    /// <summary>How long current-replay stays on game-scene from the match clock. Default 45 seconds.</summary>
    public TimeSpan InfoVisibleFor { get; set; } = TimeSpan.FromSeconds(45);
    public string TierDivisionSourceName { get; set; } = "tier-division";
    public string TierRankPointsSourceName { get; set; } = "rank-points";
}
