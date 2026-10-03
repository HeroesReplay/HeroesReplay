namespace HeroesReplay.Core.Obs.Recording;

/// <summary>
/// A publishable file is the path OBS finalized for a recording this process owns.
/// </summary>
public static class RecordingOwnership
{
    public static bool CanPublish(ObsRecordingResult recording, bool allowsMedia) =>
        recording != null
        && recording.Succeeded
        && recording.Owned
        && recording.Finalized
        && allowsMedia
        && !string.IsNullOrWhiteSpace(recording.OutputPath);

    public static string SelectFinalizedFile(string outputPath, string contextDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return null;
        }

        // The context directory is not searched. A newer file there is not this recording.
        if (string.IsNullOrWhiteSpace(contextDirectory))
        {
            return outputPath;
        }

        return outputPath;
    }

    public static string FileToDiscard(ObsRecordingResult recording, bool allowsMedia)
    {
        if (
            recording == null
            || allowsMedia
            || !recording.Owned
            || !recording.Finalized
            || string.IsNullOrWhiteSpace(recording.OutputPath)
        )
        {
            return null;
        }

        return recording.OutputPath;
    }
}
