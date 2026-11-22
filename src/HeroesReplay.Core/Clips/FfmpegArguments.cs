using System;
using System.Collections.Generic;
using System.Globalization;

namespace HeroesReplay.Core.Clips;

public static class FfmpegArguments
{
    /// <summary>
    /// ffmpeg seeks to the keyframe before this long ahead of the cut, then decodes to the exact
    /// frame, so a clip late in a long match does not decode the whole recording first.
    /// </summary>
    public const double SeekLeadSeconds = 30;

    public static bool FitsRecording(double fileStart, double clipDuration, double recordingSeconds)
    {
        return recordingSeconds > 1
            && fileStart >= 0
            && clipDuration >= 0.5
            && fileStart + 1 < recordingSeconds;
    }

    /// <summary>
    /// The clip's length inside the recording: <paramref name="clipDuration"/>, or what is left of
    /// the file after <paramref name="fileStart"/> when the recording ends before the clip does.
    /// </summary>
    public static double DurationInside(
        double fileStart,
        double clipDuration,
        double recordingSeconds
    ) => Math.Min(clipDuration, recordingSeconds - fileStart);

    /// <summary>
    /// ffprobe arguments that print only the container duration in seconds. The container is
    /// read from the file, never forced, so a plain or a fragmented MP4 (#310) is read the same way.
    /// </summary>
    public static string[] ProbeDuration(string input) =>
        ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", input];

    public static string[] Cut(
        string input,
        string output,
        double startSeconds,
        double durationSeconds
    )
    {
        // Two seeks: a fast one on the input to the keyframe before the lead, then an exact one
        // on the output. The cut stays frame-accurate, and only the lead is decoded and dropped.
        CultureInfo invariant = CultureInfo.InvariantCulture;
        double coarse = Math.Max(0, startSeconds - SeekLeadSeconds);
        var arguments = new List<string> { "-y" };
        if (coarse > 0)
        {
            arguments.Add("-ss");
            arguments.Add(coarse.ToString("0.###", invariant));
        }

        arguments.AddRange(
            new[]
            {
                "-i",
                input,
                "-ss",
                (startSeconds - coarse).ToString("0.###", invariant),
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
            }
        );
        return arguments.ToArray();
    }
}
