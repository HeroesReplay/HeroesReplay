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
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Clips;

public static class MatchClipExporter
{
    public static async Task ExportAsync(
        Replay replay,
        int? replayId,
        string contextDirectory,
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

        string match = await WaitForMatchFileAsync(contextDirectory).ConfigureAwait(false);
        if (match == null)
        {
            logger?.LogWarning(
                "No match recording in {Directory}. Pentakill clips were not cut.",
                contextDirectory
            );
            return;
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

            string folderName = SafeName($"{clip.Kind}-{clip.Hero}-{clip.HudStartSecond}");
            string folder = Path.Combine(clipRoot, folderName);
            Directory.CreateDirectory(folder);
            string output = Path.Combine(folder, "clip.mp4");
            if (File.Exists(output))
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
        string title = string.Join(" - ", new[] { clip.Hero, clip.Kind, map, id }.WhereNotEmpty());
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

    private static async Task<string> WaitForMatchFileAsync(string contextDirectory)
    {
        DirectoryInfo directory = new(contextDirectory);
        if (!directory.Exists)
        {
            return null;
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            FileInfo[] files = directory.GetFiles("*.mp4", SearchOption.TopDirectoryOnly);
            FileInfo newest = null;
            foreach (FileInfo file in files)
            {
                if (newest == null || file.Length > newest.Length)
                {
                    newest = file;
                }
            }

            if (newest != null && newest.Length > 0 && CanRead(newest.FullName))
            {
                return newest.FullName;
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

    private static async Task<bool> CutAsync(
        string input,
        string output,
        double start,
        double duration
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
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
            return process.ExitCode == 0 && File.Exists(output);
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
