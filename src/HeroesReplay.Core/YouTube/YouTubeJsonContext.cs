using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeroesReplay.Core.YouTube.Outbox;
using HeroesReplay.Core.YouTube.Playlists;
using HeroesReplay.Core.YouTube.Publication;
using HeroesReplay.Core.YouTube.Quota;

namespace HeroesReplay.Core.YouTube;

/// <summary>Source-generated JSON for the YouTube files (#454): no reflection, so Native AOT reads and writes them.</summary>
[JsonSerializable(typeof(YouTubeEntry))]
[JsonSerializable(typeof(Dictionary<string, InterruptedUploadRetries.Entry>))]
[JsonSerializable(typeof(UploadAttemptManifest))]
[JsonSerializable(typeof(UploadAttemptPolicy))]
[JsonSerializable(typeof(YouTubeLibraryVideo))]
[JsonSerializable(typeof(YouTubePlaylistCache))]
[JsonSerializable(typeof(YouTubeLibraryDryRun))]
[JsonSerializable(typeof(YouTubeUploadsIndex))]
[JsonSerializable(typeof(PublicationLedger))]
[JsonSerializable(typeof(YouTubeQuotaDay))]
[JsonSerializable(typeof(YouTubeDryRunReceipt))]
[JsonSerializable(typeof(ParkedRecording))]
internal sealed partial class YouTubeJsonContext : JsonSerializerContext
{
    /// <summary>Most YouTube files: indented, with the default names. Reads use <see cref="JsonSerializerContext"/>'s <c>Default</c>.</summary>
    public static YouTubeJsonContext Indented { get; } =
        new(new JsonSerializerOptions { WriteIndented = true });
}
