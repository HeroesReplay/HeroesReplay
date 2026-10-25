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
}
