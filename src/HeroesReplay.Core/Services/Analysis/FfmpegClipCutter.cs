using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Analysis;

public sealed class FfmpegCutResult
{
    public string OutputPath { get; set; }
    public bool Ok { get; set; }
    public string Detail { get; set; }
}

public static class FfmpegClipCutter
{
    public static string Tool(string name)
    {
        string file = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name
            : name + ".exe";
        string bundled = Path.Combine(@"C:\ffmpeg\bin", file);
        if (File.Exists(bundled))
        {
            return bundled;
        }

        return name;
    }

    public static IReadOnlyList<string> CutArguments(
        string source,
        string destination,
        double startSeconds,
        double durationSeconds
    )
    {
        return new[]
        {
            "-y",
            "-i",
            source,
            "-ss",
            Seconds(startSeconds),
            "-t",
            Seconds(durationSeconds),
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
            destination,
        };
    }

    public static IReadOnlyList<FfmpegCutResult> CutDirectory(string directory, ILogger logger)
    {
        var results = new List<FfmpegCutResult>();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return results;
        }

        string listPath = Path.Combine(directory, MatchClipList.FileName);
        IReadOnlyList<MatchClipEntry> entries;
        try
        {
            entries = MatchClipList.Read(listPath);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            logger?.LogWarning(exception, "Could not read {Path}.", listPath);
            return results;
        }

        if (entries.Count == 0)
        {
            return results;
        }

        var clipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MatchClipEntry entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry?.File))
            {
                clipNames.Add(entry.File);
            }
        }

        string source = WaitForSource(directory, clipNames);
        if (source == null)
        {
            logger?.LogWarning("No match recording in {Directory} to cut.", directory);
            return results;
        }

        if (!TryProbe(source, out int width, out int height, out _))
        {
            logger?.LogWarning("ffprobe could not read {Path}.", source);
            return results;
        }

        foreach (MatchClipEntry entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.File))
            {
                continue;
            }

            string output = Path.Combine(directory, entry.File);
            if (File.Exists(output))
            {
                continue;
            }

            double duration = entry.FileEndSeconds - entry.FileStartSeconds;
            if (duration <= 0)
            {
                continue;
            }

            FfmpegCutResult result = Cut(
                source,
                output,
                entry.FileStartSeconds,
                duration,
                width,
                height
            );
            results.Add(result);
            if (result.Ok)
            {
                logger?.LogInformation("Cut {Clip}: {Detail}", entry.File, result.Detail);
            }
            else
            {
                logger?.LogWarning("Did not cut {Clip}: {Detail}", entry.File, result.Detail);
            }
        }

        return results;
    }

    public static FfmpegCutResult Cut(
        string source,
        string output,
        double startSeconds,
        double durationSeconds,
        int sourceWidth,
        int sourceHeight
    )
    {
        if (
            Run(Tool("ffmpeg"), CutArguments(source, output, startSeconds, durationSeconds), 180000)
                != 0
            || !File.Exists(output)
        )
        {
            return new FfmpegCutResult
            {
                OutputPath = output,
                Ok = false,
                Detail = "ffmpeg did not write the clip.",
            };
        }

        if (!TryProbe(output, out int width, out int height, out double duration))
        {
            return new FfmpegCutResult
            {
                OutputPath = output,
                Ok = false,
                Detail = "ffprobe could not read the clip.",
            };
        }

        if (width != sourceWidth || height != sourceHeight)
        {
            return new FfmpegCutResult
            {
                OutputPath = output,
                Ok = false,
                Detail = $"Frame is {width}x{height}, source is {sourceWidth}x{sourceHeight}.",
            };
        }

        if (Math.Abs(duration - durationSeconds) > 1.5)
        {
            return new FfmpegCutResult
            {
                OutputPath = output,
                Ok = false,
                Detail =
                    $"Duration {duration.ToString("0.00", CultureInfo.InvariantCulture)}s is not {Seconds(durationSeconds)}s.",
            };
        }

        return new FfmpegCutResult
        {
            OutputPath = output,
            Ok = true,
            Detail = $"{width}x{height} {duration.ToString("0.00", CultureInfo.InvariantCulture)}s",
        };
    }

    private static string WaitForSource(string directory, HashSet<string> clipNames)
    {
        long lastLength = -1;
        int stable = 0;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            string candidate = null;
            long length = 0;
            foreach (string path in Directory.EnumerateFiles(directory, "*.mp4"))
            {
                if (clipNames.Contains(Path.GetFileName(path)))
                {
                    continue;
                }

                var info = new FileInfo(path);
                if (info.Length > length)
                {
                    candidate = path;
                    length = info.Length;
                }
            }

            if (candidate != null && length > 0 && length == lastLength)
            {
                stable++;
                if (stable >= 2)
                {
                    return candidate;
                }
            }
            else
            {
                stable = 0;
            }

            lastLength = length;
            Thread.Sleep(500);
        }

        return null;
    }

    public static bool TryProbe(string path, out int width, out int height, out double duration)
    {
        width = 0;
        height = 0;
        duration = 0;
        string json = Read(
            Tool("ffprobe"),
            new[]
            {
                "-v",
                "error",
                "-select_streams",
                "v:0",
                "-show_entries",
                "stream=width,height:format=duration",
                "-of",
                "json",
                path,
            },
            30000
        );
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (
                document.RootElement.TryGetProperty("streams", out JsonElement streams)
                && streams.GetArrayLength() > 0
            )
            {
                JsonElement stream = streams[0];
                width = stream.TryGetProperty("width", out JsonElement widthValue)
                    ? widthValue.GetInt32()
                    : 0;
                height = stream.TryGetProperty("height", out JsonElement heightValue)
                    ? heightValue.GetInt32()
                    : 0;
            }

            if (
                document.RootElement.TryGetProperty("format", out JsonElement format)
                && format.TryGetProperty("duration", out JsonElement durationValue)
                && double.TryParse(
                    durationValue.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double parsed
                )
            )
            {
                duration = parsed;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return width > 0 && height > 0;
    }

    private static string Seconds(double value) =>
        Math.Max(0, value).ToString("0.###", CultureInfo.InvariantCulture);

    private static int Run(
        string fileName,
        IReadOnlyList<string> arguments,
        int timeoutMilliseconds
    )
    {
        Read(fileName, arguments, timeoutMilliseconds, out int code);
        return code;
    }

    private static string Read(
        string fileName,
        IReadOnlyList<string> arguments,
        int timeoutMilliseconds
    )
    {
        return Read(fileName, arguments, timeoutMilliseconds, out _);
    }

    private static string Read(
        string fileName,
        IReadOnlyList<string> arguments,
        int timeoutMilliseconds,
        out int exitCode
    )
    {
        exitCode = -1;
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(start);
            if (process == null)
            {
                return null;
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // The process may already have exited.
                }

                return null;
            }

            exitCode = process.ExitCode;
            stderrTask.GetAwaiter().GetResult();
            return stdoutTask.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
