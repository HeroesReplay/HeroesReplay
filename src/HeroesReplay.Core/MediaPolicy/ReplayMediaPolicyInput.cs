using System;
using System.Collections.Generic;
using HeroesReplay.Core.Analysis;

namespace HeroesReplay.Core.MediaPolicy;

public sealed class ReplayMediaPlayer
{
    public int Team { get; init; }
    public string Hero { get; init; }
    public string Name { get; init; }
    public int BattleTag { get; init; }
    public bool IsAi { get; init; }
}

/// <summary>Null means completion is not known yet. A false flag is a hard miss.</summary>
public sealed class ReplayMediaCompletion
{
    public bool IsVerifiedComplete { get; init; }
}

/// <summary>Null means the recording has not been checked yet.</summary>
public sealed class ReplayMediaFinalization
{
    public bool IsFinalized { get; init; }
    public bool IsCorrelated { get; init; }
}

/// <summary>
/// Facts supplied by the caller. Rank, MMR, focus hero, roster, and events may be absent.
/// Notable events must already be <see cref="TeamKillClip"/> values from <see cref="TeamKillClips"/>.
/// </summary>
public sealed class ReplayMediaPolicyInput
{
    public int? ReplayId { get; init; }
    public DateTime? GameDateUtc { get; init; }
    public string GameVersion { get; init; }
    public string Map { get; init; }
    public string GameMode { get; init; }
    public string Rank { get; init; }
    public double? AverageMmr { get; init; }
    public IReadOnlyList<ReplayMediaPlayer> Roster { get; init; }
    public string FocusHero { get; init; }
    public bool ViewerRequested { get; init; }
    public bool RecordAndUpload { get; init; }
    public string RequestedBy { get; init; }
    public IReadOnlyList<TeamKillClip> NotableEvents { get; init; }
    public bool AlreadyPublished { get; init; }
    public bool AlreadyScheduled { get; init; }
    public bool InOutbox { get; init; }
    public ReplayMediaCompletion Completion { get; init; }
    public ReplayMediaFinalization Media { get; init; }
}
