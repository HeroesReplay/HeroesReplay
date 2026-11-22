namespace HeroesReplay.Core.Clips;

/// <summary>
/// What one session's clip pass did, for the session log and <c>status.json</c>: a pass that
/// fails is only a warning in the log, so without these counts a stack that stopped cutting clips
/// looks healthy.
/// </summary>
/// <param name="OnCamera">Clips the replay gave that the camera showed.</param>
/// <param name="NotOnCamera">Clips the replay gave that the camera did not show, so were not cut.</param>
/// <param name="Cut">Clips cut in this pass.</param>
/// <param name="Failed">Clips the camera showed that were not cut.</param>
/// <param name="Failure">Why the last of those was not cut, or null.</param>
public sealed record ClipExportResult(
    int OnCamera = 0,
    int NotOnCamera = 0,
    int Cut = 0,
    int Failed = 0,
    string Failure = null
)
{
    public static ClipExportResult None { get; } = new();

    public ClipExportResult Fail(int clips, string why) =>
        this with
        {
            Failed = Failed + clips,
            Failure = why,
        };
}
