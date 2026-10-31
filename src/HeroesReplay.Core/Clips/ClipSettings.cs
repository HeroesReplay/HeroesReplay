namespace HeroesReplay.Core.Clips;

/// <summary>
/// <c>Clips</c>. Clips (pentakills and triple kills, #511) are cut from the OBS match recording, so they run whenever
/// <c>OBS:RecordingEnabled</c> is true.
/// </summary>
public sealed class ClipSettings
{
    /// <summary>
    /// A folder holding <c>ffmpeg.exe</c> and <c>ffprobe.exe</c> that wins over every other
    /// place. Empty (the default) uses the <c>deps install</c> folder, then <c>C:\ffmpeg\bin</c>,
    /// then PATH (<see cref="FfmpegLocator"/>).
    /// </summary>
    public string FfmpegDirectory { get; set; } = string.Empty;

    /// <summary>
    /// The camera shows a clip while the hero it follows is this many map units or less from the
    /// clip's place (a boss pit) or from the player whose kills it shows (<see cref="ClipCamera"/>).
    /// A little wider than a fight at the pit (<c>Insights:Bosses:Radius</c>, 12).
    /// </summary>
    public double CameraRadius { get; set; } = 15;

    /// <summary>
    /// The least share of a clip's moment the camera must show, and it must show its end, or the
    /// clip is not cut: it would show something else (<see cref="ClipCamera"/>).
    /// </summary>
    public double CameraShare { get; set; } = 0.5;
}
