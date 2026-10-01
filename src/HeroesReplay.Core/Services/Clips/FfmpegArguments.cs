using System.Globalization;

namespace HeroesReplay.Core.Services.Clips;

public static class FfmpegArguments
{
    public static bool FitsRecording(double fileStart, double clipDuration, double recordingSeconds)
    {
        return recordingSeconds > 1
            && fileStart >= 0
            && clipDuration >= 0.5
            && fileStart + 1 < recordingSeconds;
    }

    public static string[] Cut(
        string input,
        string output,
        double startSeconds,
        double durationSeconds
    )
    {
        CultureInfo invariant = CultureInfo.InvariantCulture;
        return new[]
        {
            "-y",
            "-i",
            input,
            "-ss",
            startSeconds.ToString("0.###", invariant),
            "-t",
            durationSeconds.ToString("0.###", invariant),
            "-c:v",
            "libx264",
            "-crf",
            "20",
            "-preset",
            "veryfast",
            "-c:a",
            "aac",
            "-b:a",
            "160k",
            "-movflags",
            "+faststart",
            output,
        };
    }
}
