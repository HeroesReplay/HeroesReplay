using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.YouTube.Search;

/// <summary>
/// What the last uploads listing saw, stored next to the replay catalog. A restarted
/// process reads <see cref="RefreshedAt"/> and does not list the channel again early.
/// </summary>
public sealed class YouTubeUploadsIndex
{
    public const string FileName = "youtube-uploads-index.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public DateTimeOffset? RefreshedAt { get; set; }
    public string PlaylistId { get; set; }
    public List<string> VideoIds { get; set; } = new();

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

    public bool IsFresh(DateTimeOffset utcNow, TimeSpan interval) =>
        RefreshedAt is DateTimeOffset at && at <= utcNow && utcNow - at < interval;
}
