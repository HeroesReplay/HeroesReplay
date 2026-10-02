using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.YouTube.Playlists;

/// <summary>
/// A channel video whose map, mode, or build is not known yet. It is looked up again at
/// <see cref="NextAttemptAt"/>, not on every pass.
/// </summary>
public sealed class YouTubeUnresolvedVideo
{
    public YouTubeLibraryVideo Video { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
}

/// <summary>
/// What the library pass knows about the channel's uploads playlist, in
/// <c>Data\youtube-uploads-index.json</c>. A restarted process reads <see cref="LastRunAt"/>
/// and does not run the pass again early.
/// </summary>
public sealed class YouTubeUploadsIndex
{
    public const string FileName = "youtube-uploads-index.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public DateTimeOffset? LastRunAt { get; set; }
    public string PlaylistId { get; set; }

    /// <summary>
    /// True once a listing reached the last page. Until then a listing does not stop early.
    /// </summary>
    public bool ListedToEnd { get; set; }

    /// <summary>
    /// The <see cref="YouTubeVideoFacts.Version"/> of the last listing that reached the last
    /// page. An older number lists every page once more.
    /// </summary>
    public int FactsVersion { get; set; }
    public List<string> VideoIds { get; set; } = new();
    public List<YouTubeUnresolvedVideo> Unresolved { get; set; } = new();

    public static string PathFor(string dataDirectory) =>
        string.IsNullOrWhiteSpace(dataDirectory) ? null : Path.Combine(dataDirectory, FileName);

    public static YouTubeUploadsIndex Load(string path)
    {
        string json = DurableFile.ReadOrAside(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new YouTubeUploadsIndex();
        }

        try
        {
            YouTubeUploadsIndex index = JsonSerializer.Deserialize<YouTubeUploadsIndex>(json);
            if (index == null)
            {
                return new YouTubeUploadsIndex();
            }

            index.VideoIds ??= new List<string>();
            index.Unresolved ??= new List<YouTubeUnresolvedVideo>();
            index.Unresolved.RemoveAll(item => string.IsNullOrWhiteSpace(item?.Video?.VideoId));
            return index;
        }
        catch (JsonException)
        {
            DurableFile.Aside(path);
            return new YouTubeUploadsIndex();
        }
    }

    public void Save(string path) =>
        DurableFile.Replace(path, JsonSerializer.Serialize(this, Options));

    public bool IsDue(DateTimeOffset utcNow, TimeSpan interval) =>
        LastRunAt is not DateTimeOffset at || at > utcNow || utcNow - at >= interval;

    /// <summary>
    /// 6 hours after the first miss, doubling to at most 7 days.
    /// </summary>
    public static TimeSpan RetryAfter(int attempts)
    {
        double hours = 6 * Math.Pow(2, Math.Max(0, attempts - 1));
        return TimeSpan.FromHours(Math.Min(hours, TimeSpan.FromDays(7).TotalHours));
    }
}
