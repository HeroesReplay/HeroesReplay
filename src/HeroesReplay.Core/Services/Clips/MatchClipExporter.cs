using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Analysis;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using HeroesReplay.Core.Services.YouTube;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Clips;

public static class MatchClipExporter
{
    public static async Task ExportAsync(
        Replay replay,
        int? replayId,
        string contextDirectory,
        string recordingPath,
        RecordingClock clock,
        YouTubeSettings youtube,
        string entryFileName,
        ILogger logger
    )
    {
        if (
            replay == null
            || clock == null
            || !clock.IsRunning
            || string.IsNullOrWhiteSpace(contextDirectory)
        )
        {
            return;
        }

        IReadOnlyList<TeamKillClip> clips = TeamKillClips.Select(TeamKillDeaths.FromReplay(replay));
        if (clips.Count == 0)
        {
            return;
        }

        string match = RecordingOwnership.SelectFinalizedFile(recordingPath, contextDirectory);
        if (string.IsNullOrWhiteSpace(match))
        {
            logger?.LogWarning(
                "No finalized OBS recording for {Directory}. Pentakill clips were not cut.",
                contextDirectory
            );
            return;
        }

        match = await WaitUntilReadableAsync(match).ConfigureAwait(false);
        if (match == null)
        {
            logger?.LogWarning(
                "Finalized OBS recording {Path} was not readable. Pentakill clips were not cut.",
                recordingPath
            );
            return;
        }

        double? recordingSeconds = null;
        string probeError = null;
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            (recordingSeconds, probeError) = ProbeDurationSeconds(match);
            if (recordingSeconds != null || !RetryDurationProbe(probeError))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }

        if (recordingSeconds == null)
        {
            logger?.LogWarning(
                "Could not read the duration of {Path}. {Error}",
                match,
                string.IsNullOrWhiteSpace(probeError) ? "ffprobe returned no duration." : probeError
            );
            return;
        }

        string ownedDuration = OwnedRecordingDurationLog(match, contextDirectory, recordingSeconds);
        if (!string.IsNullOrWhiteSpace(ownedDuration))
        {
            logger?.LogInformation("{OwnedDuration}", ownedDuration);
        }

        string clipRoot = Path.Combine(contextDirectory, "clips");
        Directory.CreateDirectory(clipRoot);
        var written = new List<object>();
        foreach (TeamKillClip clip in clips)
        {
            if (
                !clock.TryMap(
                    clip.HudStartSecond,
                    clip.HudEndSecond,
                    out double start,
                    out double duration
                )
            )
            {
                logger?.LogInformation(
                    "No recording time for {Kind} {Hero} at HUD {Start}-{End}.",
                    clip.Kind,
                    clip.Hero,
                    clip.HudStartSecond,
                    clip.HudEndSecond
                );
                continue;
            }

            if (!FfmpegArguments.FitsRecording(start, duration, recordingSeconds.Value))
            {
                logger?.LogWarning(
                    "Recording is {Recording:0.0}s. {Kind} {Hero} maps to {Start:0.0}s, after the file ends.",
                    recordingSeconds.Value,
                    clip.Kind,
                    clip.Hero,
                    start
                );
                continue;
            }

            string folderName = SafeName($"{clip.Kind}-{clip.Hero}-{clip.HudStartSecond}");
            string folder = Path.Combine(clipRoot, folderName);
            Directory.CreateDirectory(folder);
            string output = Path.Combine(folder, "clip.mp4");
            if (File.Exists(output) && new FileInfo(output).Length > 10_000)
            {
                written.Add(Row(clip, start, duration, output));
                continue;
            }

            bool cut = await CutAsync(match, output, start, duration).ConfigureAwait(false);
            if (!cut)
            {
                logger?.LogWarning("ffmpeg did not cut {Path}.", output);
                continue;
            }

            WriteEntry(folder, entryFileName, clip, replay, replayId, youtube);
            written.Add(Row(clip, start, duration, output));
            logger?.LogInformation(
                "Cut {Kind} {Hero} at file {Start:0.0}s for {Duration:0.0}s.",
                clip.Kind,
                clip.Hero,
                start,
                duration
            );
        }

        if (written.Count == 0)
        {
            return;
        }

        string index = Path.Combine(contextDirectory, "clips.json");
        await File.WriteAllTextAsync(
                index,
                JsonSerializer.Serialize(
                    written,
                    new JsonSerializerOptions { WriteIndented = true }
                )
            )
            .ConfigureAwait(false);
    }

    private static object Row(TeamKillClip clip, double start, double duration, string output) =>
        new
        {
            kind = clip.Kind,
            hero = clip.Hero,
            description = clip.Description,
            hudStart = clip.HudStartSecond,
            hudEnd = clip.HudEndSecond,
            fileStart = start,
            duration,
            file = output,
        };

    private static void WriteEntry(
        string folder,
        string entryFileName,
        TeamKillClip clip,
        Replay replay,
        int? replayId,
        YouTubeSettings youtube
    )
    {
        string name = string.IsNullOrWhiteSpace(entryFileName)
            ? "youtube-entry.json"
            : entryFileName;
        string id = replayId is > 0 ? replayId.Value.ToString(CultureInfo.InvariantCulture) : null;
        string map = replay?.Map;
        string title = YouTubeListing.ApplyMarker(
            string.Join(" - ", new[] { clip.Hero, clip.Kind, map, id }.WhereNotEmpty()),
            youtube?.TitlePrefix
        );
        var entry = new YouTubeEntry
        {
            ReplayId = replayId,
            Map = map,
            Title = title,
            PrivacyStatus = youtube?.PrivacyStatus ?? "public",
            CategoryId = youtube?.CategoryId,
            DescriptionLines = new[]
            {
                $"clip:{id}:{clip.Kind}:{clip.Hero}",
                clip.Description,
                id == null
                    ? string.Empty
                    : $"Heroes Profile Match: https://www.heroesprofile.com/Match/Single/?replayID={id}",
            }.WhereNotEmpty(),
            Tags = new[] { clip.Kind, clip.Hero, map }.WhereNotEmpty(),
            Hero = string.IsNullOrWhiteSpace(clip.Hero) ? null : clip.Hero.Trim(),
            Heroes = string.IsNullOrWhiteSpace(clip.Hero) ? null : new[] { clip.Hero.Trim() },
        };
        File.WriteAllText(
            Path.Combine(folder, name),
            JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true })
        );
    }

    private static string[] WhereNotEmpty(this IEnumerable<string> parts)
    {
        var kept = new List<string>();
        if (parts == null)
        {
            return Array.Empty<string>();
        }

        foreach (string part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                kept.Add(part);
            }
        }

        return kept.ToArray();
    }

    private static async Task<string> WaitUntilReadableAsync(string recordingPath)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (CanRead(recordingPath))
            {
                return recordingPath;
            }

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }

        return null;
    }

    private static bool CanRead(string path)
    {
        try
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite
            );
            return stream.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static bool RetryDurationProbe(string error) => !IsMissingTool(error);

    public static string OwnedRecordingDurationLog(
        string outputPath,
        string contextDirectory,
        double? durationSeconds
    )
    {
        string owned = RecordingOwnership.SelectFinalizedFile(outputPath, contextDirectory);
        if (
            string.IsNullOrWhiteSpace(owned)
            || durationSeconds is not double seconds
            || seconds < 0
        )
        {
            return null;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "Read duration {0:0.0}s of owned recording {1}.",
            seconds,
            owned
        );
    }

    private static bool IsMissingTool(string error) =>
        !string.IsNullOrWhiteSpace(error)
        && (
            error.Contains("ffprobe was not found", StringComparison.OrdinalIgnoreCase)
            || error.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase)
            || error.Contains("ffprobe did not start", StringComparison.OrdinalIgnoreCase)
        );

    private static (double? Seconds, string Error) ProbeDurationSeconds(string path)
    {
        string ffprobe = FfmpegLocator.Find("ffprobe");
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("format=duration");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("csv=p=0");
        startInfo.ArgumentList.Add(path);
        try
        {
            using Process process = Process.Start(startInfo);
            if (process == null)
            {
                return (null, "ffprobe did not start.");
            }

            string text = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (
                process.ExitCode == 0
                && double.TryParse(
                    text.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double seconds
                )
            )
            {
                return (seconds, null);
            }

            string detail = string.IsNullOrWhiteSpace(error) ? text : error;
            return (
                null,
                string.IsNullOrWhiteSpace(detail) ? "ffprobe returned no duration." : detail.Trim()
            );
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is IOException)
        {
            return (null, "ffprobe was not found. " + ex.Message);
        }
    }

    private static async Task<bool> CutAsync(
        string input,
        string output,
        double start,
        double duration
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FfmpegLocator.Find("ffmpeg"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        foreach (string argument in FfmpegArguments.Cut(input, output, start, duration))
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (
                process.ExitCode != 0
                || !File.Exists(output)
                || new FileInfo(output).Length < 10_000
            )
            {
                if (File.Exists(output))
                {
                    File.Delete(output);
                }

                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is IOException)
        {
            return false;
        }
    }

    private static string SafeName(string value)
    {
        var chars = new char[value.Length];
        int n = 0;
        foreach (char c in value)
        {
            chars[n++] = char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-';
        }

        string name = new string(chars, 0, n).Trim('-');
        return string.IsNullOrWhiteSpace(name) ? "clip" : name;
    }
}
