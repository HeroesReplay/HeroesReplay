using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;

namespace HeroesReplay.Core.Services.YouTube;

public static class YouTubeEntryWriter
{
    public static async Task<bool> WriteIfAllowedAsync(
        string directory,
        string fileName,
        LoadedReplay loaded,
        YouTubeSettings youtube,
        bool isCompleteRecording,
        CancellationToken cancellationToken
    )
    {
        if (!SessionMedia.ShouldWriteYouTubeEntry(youtube, loaded))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        YouTubeEntry entry = YouTubeEntryBuilder.Create(loaded, youtube, isCompleteRecording);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        string json = JsonSerializer.Serialize(
            entry,
            new JsonSerializerOptions { WriteIndented = true }
        );
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
