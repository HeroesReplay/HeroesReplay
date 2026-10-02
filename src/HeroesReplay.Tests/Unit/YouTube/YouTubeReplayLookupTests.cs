using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Publication;
using HeroesReplay.Core.YouTube.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class YouTubeReplayLookupTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "hr-yt-lookup-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AlreadyUploaded_BuildsTheIndexFromEveryPage()
    {
        var listing = new FakeListing(
            Page("p2", Video("v3", "Volskaya Foundry - Storm League - Diamond - 65550003")),
            Page("p3", Video("v2", "Tomb of the Spider Queen - 65389750 - Storm League - Gold 3")),
            Page(
                null,
                Video(
                    "v1",
                    "Li-Ming - pentakill - Alterac Pass",
                    "Heroes Profile Match: https://www.heroesprofile.com/Match/Single/?replayID=65550001"
                )
            )
        );
        YouTubeReplayLookup lookup = Lookup(listing);

        Assert.True(await lookup.AlreadyUploadedAsync(Replay(65389750), CancellationToken.None));

        Assert.Equal(1, listing.PlaylistCalls);
        Assert.Equal(3, listing.PageCalls);
        string catalog = YouTubeReplayCatalog.PathFor(directory);
        Assert.True(YouTubeReplayCatalog.Contains(catalog, 65550003));
        Assert.True(YouTubeReplayCatalog.Contains(catalog, 65550001));
        YouTubeUploadsIndex index = YouTubeUploadsIndex.Load(
            YouTubeUploadsIndex.PathFor(directory)
        );
        Assert.Equal("UUuploads", index.PlaylistId);
        Assert.Equal(new[] { "v1", "v2", "v3" }, index.VideoIds.OrderBy(id => id));
        Assert.NotNull(index.RefreshedAt);
    }

    [Fact]
    public async Task AlreadyUploaded_StopsOnAPageWithOnlyKnownVideos()
    {
        new YouTubeUploadsIndex
        {
            PlaylistId = "UUuploads",
            VideoIds = new List<string> { "v2", "v1" },
            RefreshedAt = DateTimeOffset.UtcNow.AddDays(-1),
        }.Save(YouTubeUploadsIndex.PathFor(directory));
        var listing = new FakeListing(
            Page("p2", Video("v3", "Cursed Hollow - Storm League - 65550003"), Video("v2", "")),
            Page("p3", Video("v2", ""), Video("v1", "")),
            Page(null, Video("v0", "Dragon Shire - Storm League - 65000000"))
        );
        YouTubeReplayLookup lookup = Lookup(listing);

        Assert.True(await lookup.AlreadyUploadedAsync(Replay(65550003), CancellationToken.None));

        Assert.Equal(0, listing.PlaylistCalls);
        Assert.Equal(2, listing.PageCalls);
        Assert.False(
            YouTubeReplayCatalog.Contains(YouTubeReplayCatalog.PathFor(directory), 65000000)
        );
    }

    [Fact]
    public async Task AlreadyUploaded_KeepsTheRefreshIntervalAcrossARestart()
    {
        var first = new FakeListing(Page(null, Video("v1", "Map - Storm League - 65550001")));
        Assert.False(
            await Lookup(first).AlreadyUploadedAsync(Replay(65550002), CancellationToken.None)
        );
        Assert.Equal(1, first.PageCalls);

        var restarted = new FakeListing(Page(null, Video("v2", "Map - Storm League - 65550002")));
        Assert.False(
            await Lookup(restarted).AlreadyUploadedAsync(Replay(65550002), CancellationToken.None)
        );
        Assert.Equal(0, restarted.PlaylistCalls);
        Assert.Equal(0, restarted.PageCalls);

        var due = new FakeListing(Page(null, Video("v2", "Map - Storm League - 65550002")));
        Assert.True(
            await Lookup(due, refresh: TimeSpan.Zero)
                .AlreadyUploadedAsync(Replay(65550002), CancellationToken.None)
        );
        Assert.Equal(1, due.PageCalls);
    }

    [Fact]
    public async Task AlreadyUploaded_IgnoresALongerNumber()
    {
        var listing = new FakeListing(
            Page(null, Video("v1", "Map - 653897501 - Storm League", ""))
        );

        Assert.False(
            await Lookup(listing).AlreadyUploadedAsync(Replay(65389750), CancellationToken.None)
        );
    }

    [Fact]
    public async Task AlreadyUploaded_PausesListingAfterTheQuotaResponse()
    {
        var listing = new FakeListing { Quota = true };
        YouTubeReplayLookup lookup = Lookup(listing, refresh: TimeSpan.Zero);
        var replay = Replay(65542021);

        Assert.False(await lookup.AlreadyUploadedAsync(replay, CancellationToken.None));
        Assert.False(await lookup.AlreadyUploadedAsync(replay, CancellationToken.None));

        Assert.Equal(1, listing.PlaylistCalls);
        Assert.False(File.Exists(YouTubeUploadsIndex.PathFor(directory)));
    }

    [Fact]
    public async Task AlreadyUploaded_DryRunNeverListsTheChannel()
    {
        var listing = new FakeListing(Page(null, Video("v1", "Map - Storm League - 65550001")));
        YouTubeReplayLookup lookup = Lookup(listing, dryRun: true, refresh: TimeSpan.Zero);

        Assert.False(await lookup.AlreadyUploadedAsync(Replay(65550001), CancellationToken.None));

        Assert.Equal(0, listing.PlaylistCalls);
        Assert.Equal(0, listing.PageCalls);
    }

    [Fact]
    public async Task AlreadyUploaded_UsesTheCatalogBeforeTheListing()
    {
        YouTubeReplayCatalog.Remember(YouTubeReplayCatalog.PathFor(directory), 65550001);
        var listing = new FakeListing();

        Assert.True(
            await Lookup(listing).AlreadyUploadedAsync(Replay(65550001), CancellationToken.None)
        );
        Assert.Equal(0, listing.PlaylistCalls);
    }

    [Fact]
    public void ListQuota_ResumesOnTheNextPacificDay()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);
        Assert.True(YouTubeListQuota.IsExhausted(new InvalidOperationException("Quota exceeded")));
        Assert.False(YouTubeListQuota.IsExhausted(new InvalidOperationException("socket closed")));
        Assert.Equal(
            PublicationSchedule.QuotaDayStart(now).AddDays(1),
            YouTubeListQuota.ResumeAt(now)
        );
    }

    private YouTubeReplayLookup Lookup(
        IYouTubeUploadsListing listing,
        bool dryRun = false,
        TimeSpan? refresh = null
    ) =>
        new(
            NullLogger<YouTubeReplayLookup>.Instance,
            new AppSettings
            {
                Location = new LocationSettings { DataDirectory = directory },
                YouTube = new YouTubeSettings
                {
                    DryRun = dryRun,
                    EntryFileNameUploaded = "youtube-entry-uploaded.json",
                    UploadsIndexRefresh = refresh ?? TimeSpan.FromHours(6),
                },
            },
            listing
        );

    private static LoadedReplay Replay(int id) => new() { ReplayId = id };

    private static YouTubeUploadedVideo Video(string id, string title, string description = "") =>
        new()
        {
            VideoId = id,
            Title = title,
            Description = description,
        };

    private static YouTubeUploadsPage Page(string next, params YouTubeUploadedVideo[] videos) =>
        new() { NextPageToken = next, Videos = videos };

    private sealed class FakeListing : IYouTubeUploadsListing
    {
        private readonly Queue<YouTubeUploadsPage> pages;

        public FakeListing(params YouTubeUploadsPage[] pages)
        {
            this.pages = new Queue<YouTubeUploadsPage>(pages);
        }

        public bool Quota { get; init; }
        public int PlaylistCalls { get; private set; }
        public int PageCalls { get; private set; }

        public Task<string> UploadsPlaylistIdAsync(CancellationToken cancellationToken)
        {
            PlaylistCalls++;
            if (Quota)
            {
                throw new InvalidOperationException(
                    "HttpStatusCode is Forbidden. The request cannot be completed because you have exceeded your quota. [quotaExceeded]"
                );
            }

            return Task.FromResult("UUuploads");
        }

        public Task<YouTubeUploadsPage> PageAsync(
            string playlistId,
            string pageToken,
            CancellationToken cancellationToken
        )
        {
            PageCalls++;
            Assert.Equal("UUuploads", playlistId);
            return Task.FromResult(pages.Dequeue());
        }
    }
}
