using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.YouTube.Outbox;

namespace HeroesReplay.Core.YouTube;

public static class PendingYouTubeUpload
{
    public static IReadOnlyList<string> Find(
        string contextsDirectory,
        string entryFileName,
        string uploadedFileName
    )
    {
        return Find(contextsDirectory, entryFileName, uploadedFileName, null);
    }

    public static IReadOnlyList<string> Find(
        string contextsDirectory,
        string entryFileName,
        string uploadedFileName,
        string attemptsDirectory
    )
    {
        var found = new List<string>();
        if (
            string.IsNullOrWhiteSpace(contextsDirectory)
            || string.IsNullOrWhiteSpace(entryFileName)
            || !Directory.Exists(contextsDirectory)
        )
        {
            return found;
        }

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(attemptsDirectory) && Directory.Exists(attemptsDirectory))
        {
            foreach (string attemptDirectory in Directory.GetDirectories(attemptsDirectory))
            {
                string bound = ReadBoundMedia(attemptDirectory);
                string context = ContextContaining(contextsDirectory, bound);
                if (context == null || !AwaitingUpload(context, entryFileName, uploadedFileName))
                {
                    continue;
                }

                if (!claimed.Add(Path.GetFullPath(context)))
                {
                    continue;
                }

                found.Add(bound);
            }
        }

        foreach (string directory in Directory.GetDirectories(contextsDirectory))
        {
            if (claimed.Contains(Path.GetFullPath(directory)))
            {
                continue;
            }

            if (!AwaitingUpload(directory, entryFileName, uploadedFileName))
            {
                continue;
            }

            string local = ReadBoundMedia(directory);
            if (File.Exists(local))
            {
                found.Add(local);
                continue;
            }

            string newest = Directory
                .GetFiles(directory, "*.mp4", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(newest))
            {
                found.Add(newest);
            }
        }

        found.Sort(
            (left, right) =>
                File.GetLastWriteTimeUtc(left).CompareTo(File.GetLastWriteTimeUtc(right))
        );
        return found;
    }

    /// <summary>
    /// A viewer request is sent first, so it reserves the earliest free publish time. The
    /// rest keep their order, oldest recording first.
    /// </summary>
    public static IReadOnlyList<string> RequestsFirst(
        IReadOnlyList<string> recordings,
        string entryFileName
    )
    {
        var requests = new List<string>();
        var others = new List<string>();
        if (recordings == null)
        {
            return requests;
        }

        foreach (string recording in recordings)
        {
            if (IsRequested(recording, entryFileName))
            {
                requests.Add(recording);
            }
            else
            {
                others.Add(recording);
            }
        }

        requests.AddRange(others);
        return requests;
    }

    /// <summary>
    /// Viewer requests with a recording on disk that still need a <c>videos.insert</c>: the entry
    /// says <c>Requested</c> and has no video id yet. The context of
    /// <paramref name="excludingRecording"/> is not counted.
    /// </summary>
    public static int CountRequestsWaiting(
        string contextsDirectory,
        string entryFileName,
        string uploadedFileName,
        string excludingRecording
    )
    {
        if (
            string.IsNullOrWhiteSpace(contextsDirectory)
            || string.IsNullOrWhiteSpace(entryFileName)
            || !Directory.Exists(contextsDirectory)
        )
        {
            return 0;
        }

        string excluded = string.IsNullOrWhiteSpace(excludingRecording)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(excludingRecording));
        int waiting = 0;
        foreach (string directory in Directory.GetDirectories(contextsDirectory))
        {
            if (
                excluded != null
                && string.Equals(
                    Path.GetFullPath(directory),
                    excluded,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                continue;
            }

            if (
                !AwaitingUpload(directory, entryFileName, uploadedFileName)
                || Directory.GetFiles(directory, "*.mp4", SearchOption.TopDirectoryOnly).Length == 0
            )
            {
                continue;
            }

            YouTubeEntry entry = ReadEntry(Path.Combine(directory, entryFileName));
            if (entry?.Requested == true && string.IsNullOrWhiteSpace(entry.VideoId))
            {
                waiting++;
            }
        }

        return waiting;
    }

    internal static YouTubeEntry ReadEntry(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<YouTubeEntry>(File.ReadAllText(path))
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool IsRequested(string recording, string entryFileName)
    {
        string directory = Path.GetDirectoryName(recording);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(entryFileName))
        {
            return false;
        }

        return ReadEntry(Path.Combine(directory, entryFileName))?.Requested == true;
    }

    public static string AttemptsDirectory(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return null;
        }

        return Path.Combine(dataDirectory, MediaPolicyAttemptLog.AttemptsDirectoryName);
    }

    private static bool AwaitingUpload(
        string directory,
        string entryFileName,
        string uploadedFileName
    )
    {
        if (!File.Exists(Path.Combine(directory, entryFileName)))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(uploadedFileName)
            || !File.Exists(Path.Combine(directory, uploadedFileName));
    }

    private static string ContextContaining(string contextsDirectory, string mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
        {
            return null;
        }

        string directory = Path.GetDirectoryName(mediaPath);
        string parent = string.IsNullOrWhiteSpace(directory)
            ? null
            : Path.GetDirectoryName(directory);
        if (
            parent == null
            || !string.Equals(
                Path.GetFullPath(parent),
                Path.GetFullPath(contextsDirectory),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return null;
        }

        return directory;
    }

    private static string ReadBoundMedia(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        string path = Path.Combine(directory, UploadAttemptStore.ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            UploadAttemptResult read = UploadAttemptManifestCodec.Read(
                File.ReadAllText(path),
                null
            );
            if (!read.Succeeded)
            {
                return null;
            }

            return UploadAttemptMachine.SelectBoundMedia(read.Manifest);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
