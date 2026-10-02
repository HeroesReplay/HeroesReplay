using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Spectating;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Playlists;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Playlists;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubeLibraryTests
{
    [Fact]
    public void Select_SkipsEntriesWithoutAVideoIdOrKnownMode()
    {
        IReadOnlyList<YouTubeLibraryItem> items = YouTubeLibraryPlanner.Select(
            new[]
            {
                new YouTubeEntry
                {
                    ReplayId = 1,
                    Map = "Cursed Hollow",
                    GameType = "Quick Match",
                },
                new YouTubeEntry
                {
                    ReplayId = 2,
                    VideoId = "abc",
                    Map = "Cursed Hollow",
                    GameType = "Custom",
                },
                new YouTubeEntry
                {
                    ReplayId = 3,
                    VideoId = "vid-1",
                    Map = "Cursed Hollow",
                    GameType = "Quick Match",
                },
                new YouTubeEntry
                {
                    ReplayId = 4,
                    VideoId = "vid-1",
                    Map = "Sky Temple",
                    GameType = "ARAM",
                },
            }
        );

        YouTubeLibraryItem item = Assert.Single(items);
        Assert.Equal("Cursed Hollow - Quick Match", item.PlaylistTitle);
        Assert.Equal("vid-1", item.VideoId);
        Assert.Equal(3, item.ReplayId);
    }

    [Fact]
    public async Task RunOnce_DryRun_WritesThePlanAndDoesNotCallYouTube()
    {
        string directory = TempDirectory();
        try
        {
            WriteEntry(
                directory,
                new YouTubeEntry
                {
                    ReplayId = 42,
                    VideoId = "video-42",
                    Map = "Sky Temple",
                    GameType = "Storm League",
                    Rank = "Platinum 1",
                }
            );
            var client = new RecordingPlaylistClient();
            var library = Library(directory, dryRun: true, client);

            int code = await library.RunOnceAsync(CancellationToken.None);

            Assert.Equal(0, code);
            Assert.Empty(client.Inserted);
            string plan = await File.ReadAllTextAsync(
                Path.Combine(directory, YouTubeLibrary.DryRunFileName)
            );
            Assert.Contains("\"Simulated\": true", plan, StringComparison.Ordinal);
            Assert.Contains("Sky Temple - Storm League - Platinum", plan, StringComparison.Ordinal);
            Assert.Contains("video-42", plan, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(directory, YouTubeLibrary.CacheFileName)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunOnce_FilesAVideoOnce()
    {
        string directory = TempDirectory();
        try
        {
            WriteEntry(
                directory,
                new YouTubeEntry
                {
                    ReplayId = 7,
                    VideoId = "video-7",
                    Map = "Dragon Shire",
                    GameType = "ARAM",
                }
            );
            var client = new RecordingPlaylistClient();
            var library = Library(directory, dryRun: false, client);

            await library.RunOnceAsync(CancellationToken.None);
            await library.RunOnceAsync(CancellationToken.None);

            Assert.Equal(("pl-0", "video-7"), Assert.Single(client.Inserted));
            Assert.Equal("Dragon Shire - ARAM", Assert.Single(client.Created));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunOnce_FilesOneVideoIntoTheMapPlaylistAndThePatchPlaylist()
    {
        string directory = TempDirectory();
        try
        {
            WriteEntry(
                directory,
                new YouTubeEntry
                {
                    ReplayId = 9,
                    VideoId = "video-9",
                    Map = "Cursed Hollow",
                    GameType = "Quick Match",
                    PrivacyStatus = "private",
                    ActualPrivacyStatus = "public",
                    GameVersion = "2.57.0.98304",
                }
            );
            var client = new RecordingPlaylistClient { FailOnInsert = 2 };
            var library = new YouTubeLibrary(
                NullLogger<YouTubeLibrary>.Instance,
                new AppSettings
                {
                    Location = new LocationSettings { DataDirectory = directory },
                    Spectate = new SpectateSettings { MinimumGameVersion = "2.57.0.98304" },
                    YouTube = new YouTubeSettings
                    {
                        DryRun = false,
                        EntryFileNameUploaded = "youtube-entry-uploaded.json",
                        SeasonName = "Season 2026",
                    },
                },
                client
            );

            await library.RunOnceAsync(CancellationToken.None);

            Assert.Equal(("pl-0", "video-9"), client.Inserted[0]);
            Assert.Contains("Cursed Hollow - Quick Match", client.Created);
            string cache = await File.ReadAllTextAsync(
                Path.Combine(directory, YouTubeLibrary.CacheFileName)
            );
            Assert.Contains("Cursed Hollow - Quick Match", cache, StringComparison.Ordinal);
            Assert.Contains("video-9", cache, StringComparison.Ordinal);
            Assert.DoesNotContain("Season 2026", cache, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static YouTubeLibrary Library(
        string directory,
        bool dryRun,
        IYouTubePlaylistClient client
    )
    {
        return new YouTubeLibrary(
            NullLogger<YouTubeLibrary>.Instance,
            new AppSettings
            {
                Location = new LocationSettings { DataDirectory = directory },
                YouTube = new YouTubeSettings
                {
                    DryRun = dryRun,
                    EntryFileNameUploaded = "youtube-entry-uploaded.json",
                },
            },
            client
        );
    }

    private static void WriteEntry(string directory, YouTubeEntry entry)
    {
        string folder = Path.Combine(directory, "Contexts", "1");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "youtube-entry-uploaded.json"),
            JsonSerializer.Serialize(entry)
        );
    }

    private static string TempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-library-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class RecordingPlaylistClient : IYouTubePlaylistClient
    {
        public List<string> Created { get; } = new();
        public List<(string PlaylistId, string VideoId)> Inserted { get; } = new();
        public int FailOnInsert { get; set; }

        public Task<string> FindOrCreateAsync(string title, CancellationToken cancellationToken)
        {
            Created.Add(title);
            return Task.FromResult("pl-" + (Created.Count - 1));
        }

        public Task InsertAsync(
            string playlistId,
            string videoId,
            CancellationToken cancellationToken
        )
        {
            Inserted.Add((playlistId, videoId));
            if (FailOnInsert > 0 && Inserted.Count >= FailOnInsert)
            {
                throw new InvalidOperationException("playlist insert failed");
            }

            return Task.CompletedTask;
        }
    }
}
