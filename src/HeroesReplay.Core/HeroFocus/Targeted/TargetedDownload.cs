namespace HeroesReplay.Core.HeroFocus.Targeted;

/// <summary>What became of one focus target's game (#433).</summary>
public enum TargetedDownload
{
    /// <summary>Now in <c>Data\Standard</c>, waiting like any downloaded replay.</summary>
    Downloaded,

    /// <summary>Already on disk, played, deferred or set aside.</summary>
    Known,

    /// <summary>Heroes Profile does not list it.</summary>
    NotFound,

    NotStormLeague,

    /// <summary>A build spectate cannot launch: newer than the client, held, or off the patch line.</summary>
    NotLaunchable,

    /// <summary>Past its media window, so it would not be recorded.</summary>
    Old,

    /// <summary>Heroes Profile refused the download.</summary>
    Failed,
}
