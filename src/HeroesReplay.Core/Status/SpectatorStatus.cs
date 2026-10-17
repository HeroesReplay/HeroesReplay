using System;
using HeroesReplay.Core.HeroFocus.Picking;
using HeroesReplay.Core.Replays;

namespace HeroesReplay.Core.Status;

public sealed class SpectatorStatus
{
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// The .NET runtime the spectate process runs on (<c>RuntimeInformation.FrameworkDescription</c>,
    /// such as <c>.NET 10.0.13</c>, #462). Null in files from older builds.
    /// </summary>
    public string DotnetRuntime { get; set; }
    public bool SpectatorRunning { get; set; }
    public bool SnapshotStale { get; set; }
    public string Phase { get; set; } = "Idle";
    public string Timer { get; set; }
    public string GatesOpen { get; set; }
    public string CoreKilled { get; set; }
    public string SessionEnd { get; set; }
    public string Map { get; set; }
    public string ReplayPath { get; set; }
    public string ReplayVersion { get; set; }
    public int? ReplayId { get; set; }

    /// <summary>
    /// True when the viewer typed the Heroes Profile replay id. Predictions stay off
    /// because that id is already public.
    /// </summary>
    public bool SuppressPredictions { get; set; }

    /// <summary>
    /// True for a Blind Rating replay with a known rank (#411): the Twitch process opens the
    /// rating prediction (Bronze to Master) instead of Blue/Red.
    /// </summary>
    public bool RatingPrediction { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? CompletedReplayId { get; set; }
    public int? CompletedWinnerTeam { get; set; }

    /// <summary>
    /// The rating (Bronze to Master) of a completed Blind Rating replay, written with the other
    /// completion fields so the rating prediction resolves only after the match (#411).
    /// </summary>
    public string CompletedRating { get; set; }

    /// <summary>
    /// Why the last session ended. Completion fields are set only for <c>VerifiedCompleted</c>.
    /// </summary>
    public string Outcome { get; set; }

    /// <summary>
    /// When spectate began holding the next replay because the desired stream is not live (#396).
    /// <see cref="Phase"/> is <c>StreamHold</c> meanwhile. Null outside a hold.
    /// </summary>
    public DateTimeOffset? StreamHoldSince { get; set; }

    /// <summary>Why spectate holds: the stream state and OBS's detail. Null outside a hold.</summary>
    public string StreamHoldReason { get; set; }
    public bool ObsSession { get; set; }
    public bool? ConnectivityOnline { get; set; }
    public string Connectivity { get; set; }
    public bool? ObsProcessRunning { get; set; }
    public bool? ObsWebsocketIdentified { get; set; }
    public string ObsSceneDesired { get; set; }
    public string ObsSceneActual { get; set; }
    public bool? ObsStreamDesired { get; set; }

    /// <summary>True only while <see cref="ObsStreamState"/> is <c>Live</c> (#395).</summary>
    public bool? ObsStreamActive { get; set; }

    /// <summary>
    /// The stream's health from GetStreamStatus (#395): <c>Live</c>, <c>Reconnecting</c>,
    /// <c>Stalled</c> (active, not reconnecting, bytes frozen), <c>Inactive</c>, or
    /// <c>Unknown</c> (OBS not read). Null before the first read.
    /// </summary>
    public string ObsStreamState { get; set; }

    /// <summary>OBS reports the stream output active and reconnecting.</summary>
    public bool? ObsStreamReconnecting { get; set; }

    /// <summary>When the stream became <c>Reconnecting</c> or <c>Stalled</c>; null otherwise.</summary>
    public DateTimeOffset? ObsStreamStuckSince { get; set; }

    /// <summary>
    /// Why a desired stream was not started: <c>obs.stream_not_armed</c> (OBS:StreamingEnabled
    /// is true but this machine is not armed), <c>obs.profile_mismatch</c>,
    /// <c>obs.collection_mismatch</c>, or <c>obs.selection_unreadable</c>. Null otherwise.
    /// </summary>
    public string ObsStreamBlockedBy { get; set; }

    /// <summary>
    /// Why the last recording start was refused (a profile or scene collection code). Null
    /// after a recording starts.
    /// </summary>
    public string ObsRecordBlockedBy { get; set; }
    public string ObsDetail { get; set; }
    public SpectatorFocusStatus Focus { get; set; }

    /// <summary>
    /// The hero the cycle follows for this whole replay (#433): hero, build and role only, never
    /// the player. Null for any other replay. The Twitch process names it in chat once.
    /// </summary>
    public SpectatorHeroFocusStatus HeroFocus { get; set; }
}

public sealed class SpectatorHeroFocusStatus
{
    public int ReplayId { get; set; }
    public string Hero { get; set; }

    /// <summary>The guide build, only when the talents clearly match it.</summary>
    public string Build { get; set; }
    public string Role { get; set; }

    /// <summary>The replay's focus pick, or null for a request or an ordinary replay.</summary>
    public static SpectatorHeroFocusStatus From(LoadedReplay loaded)
    {
        FocusPick pick = loaded?.FocusPick;
        if (
            pick == null
            || loaded.RewardQueueItem?.Request != null
            || loaded.ReplayId is not int replayId
            || string.IsNullOrWhiteSpace(pick.Hero)
        )
        {
            return null;
        }

        return new SpectatorHeroFocusStatus
        {
            ReplayId = replayId,
            Hero = pick.Hero,
            Build = pick.Build,
            Role = pick.Role,
        };
    }
}

public sealed class SpectatorFocusStatus
{
    public int Index { get; set; }
    public string Hero { get; set; }
    public string Player { get; set; }
    public string Calculator { get; set; }
    public float Points { get; set; }
    public string Description { get; set; }
}
