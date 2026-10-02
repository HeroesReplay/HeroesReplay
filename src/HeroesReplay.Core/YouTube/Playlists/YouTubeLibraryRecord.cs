using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.YouTube.Playlists;

/// <summary>
/// One line of <c>Data\youtube-library.jsonl</c>: a video on the channel and what it shows.
/// </summary>
public sealed class YouTubeLibraryVideo
{
    public string VideoId { get; set; }
    public int? ReplayId { get; set; }
    public string Kind { get; set; }
    public string Map { get; set; }
    public string Mode { get; set; }
    public string Rank { get; set; }
    public string GameVersion { get; set; }
    public string PrivacyStatus { get; set; }
    public DateTimeOffset? UploadedAt { get; set; }

    /// <summary>
    /// The description's <c>Draft:</c> note (<c>Blue no tank, Red double healer</c>). Null for
    /// a usual draft, a clip, or a video from before the template wrote the note.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Draft { get; set; }

    /// <summary>
    /// The hero a viewer's request named (<c>{replayId},{slot}</c>), from the description's
    /// <c>Featured:</c> line. Null when the request named no player or there was no request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string FocusHero { get; set; }

    [JsonIgnore]
    public bool IsClip => string.Equals(Kind, YouTubeLibraryRecord.Clip, StringComparison.Ordinal);

    /// <summary>
    /// A viewer asked to review this replay from one player's view.
    /// </summary>
    [JsonIgnore]
    public bool IsViewerReview => !IsClip && !string.IsNullOrWhiteSpace(FocusHero);

    /// <summary>
    /// A full match needs its map, mode, and build before it is filed. A clip needs its build.
    /// </summary>
    [JsonIgnore]
    public bool IsResolved =>
        !string.IsNullOrWhiteSpace(GameVersion)
        && (IsClip || (EnglishMapNames.IsCatalog(Map) && !string.IsNullOrWhiteSpace(Mode)));
}

/// <summary>
/// The uploader appends a line for every video it inserts, and the library pass appends one
/// for every channel video it learns about. The file sits in <c>Data</c>, not a context
/// folder, so retention never deletes it. A later line for the same video wins.
/// </summary>
public static class YouTubeLibraryRecord
{
    public const string FileName = "youtube-library.jsonl";
    public const string Full = "full";
    public const string Clip = "clip";

    private static readonly object Gate = new();

    public static string PathFor(string dataDirectory) =>
        string.IsNullOrWhiteSpace(dataDirectory) ? null : Path.Combine(dataDirectory, FileName);

    /// <summary>
    /// The record line for an entry. The draft note and the named player come from the entry's
    /// description lines, the same lines the library pass reads back from YouTube.
    /// </summary>
    public static YouTubeLibraryVideo FromEntry(YouTubeEntry entry, DateTimeOffset? uploadedAt)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.VideoId))
        {
            return null;
        }

        bool clip = IsClipEntry(entry);
        string[] lines = entry.DescriptionLines ?? [];
        return new YouTubeLibraryVideo
        {
            VideoId = entry.VideoId.Trim(),
            ReplayId = entry.ReplayId,
            Kind = clip ? Clip : Full,
            Map = EnglishMapNames.Canonical(entry.Map),
            Mode = clip ? null : entry.GameType,
            Rank = entry.Rank,
            GameVersion = entry.GameVersion,
            PrivacyStatus = string.IsNullOrWhiteSpace(entry.ActualPrivacyStatus)
                ? entry.PrivacyStatus
                : entry.ActualPrivacyStatus,
            UploadedAt = uploadedAt,
            Draft = clip ? null : YouTubeVideoFacts.Draft(lines),
            FocusHero = clip ? null : YouTubeVideoFacts.FocusHero(lines),
        };
    }

    public static void Append(string path, YouTubeLibraryVideo video)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(video?.VideoId))
        {
            return;
        }

        string line = JsonSerializer.Serialize(video) + Environment.NewLine;
        lock (Gate)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // The other process may be appending at the same moment. Its write is short.
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.AppendAllText(path, line);
                    return;
                }
                catch (IOException) when (attempt < 20)
                {
                    Thread.Sleep(25);
                }
            }
        }
    }

    /// <summary>
    /// Every video in the record, keyed by video id. A line that cannot be read is skipped.
    /// </summary>
    public static Dictionary<string, YouTubeLibraryVideo> Read(string path)
    {
        var videos = new Dictionary<string, YouTubeLibraryVideo>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return videos;
        }

        string[] lines;
        lock (Gate)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite
            );
            using var reader = new StreamReader(stream);
            lines = reader.ReadToEnd().Split('\n');
        }

        foreach (string line in lines.Select(line => line.Trim()).Where(line => line.Length > 0))
        {
            try
            {
                YouTubeLibraryVideo video = JsonSerializer.Deserialize<YouTubeLibraryVideo>(line);
                if (!string.IsNullOrWhiteSpace(video?.VideoId))
                {
                    videos[video.VideoId.Trim()] = video;
                }
            }
            catch (JsonException)
            {
                // A half-written last line from a crash. The next pass appends a whole one.
            }
        }

        return videos;
    }

    public static bool IsClipEntry(YouTubeEntry entry) =>
        entry?.DescriptionLines?.FirstOrDefault()?.StartsWith("clip:", StringComparison.Ordinal)
        == true;
}
