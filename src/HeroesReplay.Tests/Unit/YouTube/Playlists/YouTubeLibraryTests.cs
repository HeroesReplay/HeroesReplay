using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google;
using Google.Apis.Requests;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Playlists;
using HeroesReplay.Core.YouTube.Publication;
using HeroesReplay.Core.YouTube.Quota;
using HeroesReplay.Core.YouTube.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Playlists;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class YouTubeLibraryTests : IDisposable
{
    private const string OldDescription =
        "Twitch: https://twitch.tv/saltysadism\r\nHeroes Profile Match: https://www.heroesprofile.com/Match/Single/?replayID={0}\r\nGame type: Storm League\r\nRank: Platinum 1\r\nHashtags: #HeroesOfTheStorm";

    private static readonly DateTimeOffset Noon = new(2026, 10, 2, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LibraryUser_IsAFileNameWithoutAColon()
    {
        // The Google token store names a file after this key. "{ChannelId}:library" became an
        // NTFS stream of the upload token's file, which each upload token refresh deleted.
        string key = YouTubeLibrary.LibraryUser(
            new AppSettings
            {
                YouTube = new YouTubeSettings { ChannelId = "UCpf5rn5UlJTUZF9n98HXS5A" },
            }
        );

        Assert.Equal("UCpf5rn5UlJTUZF9n98HXS5A-library", key);
        Assert.DoesNotContain(':', key);
        Assert.Equal(-1, key.IndexOfAny(Path.GetInvalidFileNameChars()));
        Assert.Equal("heroesreplay-library", YouTubeLibrary.LibraryUser(new AppSettings()));
    }

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "heroesreplay-library-" + Guid.NewGuid().ToString("N")
    );

    private readonly List<TimeSpan> delays = new();

    public YouTubeLibraryTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Background_WithoutTheLibraryConsent_IsSkippedBeforeAnyCallOrStamp()
    {
        var client = new FakeClient { Consent = false };

        YouTubeLibraryPass pass = await Library(client)
            .RunInBackgroundAsync(startup: true, CancellationToken.None);

        Assert.Equal("no-consent", pass.SkipCode);
        Assert.Contains("heroesreplay-library", pass.Skipped, StringComparison.Ordinal);
        Assert.Contains("youtube library --once", pass.Skipped, StringComparison.Ordinal);
        Assert.Equal(0, client.Calls);
        Assert.Null(YouTubeUploadsIndex.Load(YouTubeUploadsIndex.PathFor(directory)).LastRunAt);
    }

    [Fact]
    public async Task Operator_PassDoesNotCheckTheStoredConsent()
    {
        var client = new FakeClient { Consent = false };

        YouTubeLibraryPass pass = await Library(client).RunOnceAsync(true, CancellationToken.None);

        Assert.Null(pass.Skipped);
        Assert.Equal(0, client.ConsentChecks);
    }

    [Fact]
    public async Task Background_AtStartupRunsEvenInsideTheInterval_ThenWaitsForIt()
    {
        var client = new FakeClient();
        new YouTubeUploadsIndex { LastRunAt = Noon.AddMinutes(-10) }.Save(
            YouTubeUploadsIndex.PathFor(directory)
        );
        YouTubeLibrary library = Library(client);

        YouTubeLibraryPass startup = await library.RunInBackgroundAsync(
            startup: true,
            CancellationToken.None
        );
        YouTubeLibraryPass early = await library.RunInBackgroundAsync(
            startup: false,
            CancellationToken.None
        );

        Assert.Null(startup.Skipped);
        Assert.Equal(
            Noon,
            YouTubeUploadsIndex.Load(YouTubeUploadsIndex.PathFor(directory)).LastRunAt
        );
        Assert.Equal("not-due", early.SkipCode);
        Assert.Equal(Noon.AddHours(1), early.RetryAt);
    }

    [Fact]
    public async Task Background_AStopBeforeThePassLeavesTheLastPassTimeAlone()
    {
        var client = new FakeClient();
        new YouTubeUploadsIndex { LastRunAt = Noon.AddHours(-3) }.Save(
            YouTubeUploadsIndex.PathFor(directory)
        );
        using var stopped = new CancellationTokenSource();
        stopped.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Library(client).RunInBackgroundAsync(startup: true, stopped.Token)
        );

        Assert.Equal(
            Noon.AddHours(-3),
            YouTubeUploadsIndex.Load(YouTubeUploadsIndex.PathFor(directory)).LastRunAt
        );
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task Background_WithTheDaysUnitsSpent_IsSkippedUntilTheNextQuotaDay()
    {
        var client = new FakeClient();
        AppSettings settings = Settings(false);
        var units = new YouTubeQuotaUnits(directory, settings.YouTube);
        Assert.True(units.TrySpendLibrary(settings.YouTube.LibraryUnitsPerDay, Noon));

        YouTubeLibraryPass pass = await Library(client, settings: settings)
            .RunInBackgroundAsync(startup: true, CancellationToken.None);

        Assert.Equal("units-spent", pass.SkipCode);
        Assert.Equal(YouTubeQuotaUnits.NextQuotaDay(Noon), pass.RetryAt);
        Assert.Equal(0, client.Calls);
        Assert.Null(YouTubeUploadsIndex.Load(YouTubeUploadsIndex.PathFor(directory)).LastRunAt);
    }

    [Fact]
    public void NextPassIn_WaitsForTheNamedTimeOrOneInterval_NeverUnderAMinute()
    {
        TimeSpan hour = TimeSpan.FromHours(1);

        Assert.Equal(hour, YouTubeLibrary.NextPassIn(new YouTubeLibraryPass(), hour, Noon));
        Assert.Equal(hour, YouTubeLibrary.NextPassIn(null, hour, Noon));
        Assert.Equal(
            TimeSpan.FromMinutes(20),
            YouTubeLibrary.NextPassIn(
                new YouTubeLibraryPass { RetryAt = Noon.AddMinutes(20) },
                hour,
                Noon
            )
        );
        Assert.Equal(
            TimeSpan.FromMinutes(1),
            YouTubeLibrary.NextPassIn(
                new YouTubeLibraryPass { RetryAt = Noon.AddSeconds(5) },
                hour,
                Noon
            )
        );
    }

    [Fact]
    public void Plan_SkipsEntriesWithoutAVideoIdOrKnownMode()
    {
        IReadOnlyList<YouTubeLibraryItem> items = YouTubeLibraryPlanner.Plan(
            new[]
            {
                PublicEntry(1, null, "Cursed Hollow", "Quick Match"),
                PublicEntry(2, "abc", "Cursed Hollow", "Custom"),
                PublicEntry(3, "vid-1", "Cursed Hollow", "Quick Match"),
                PublicEntry(4, "vid-1", "Sky Temple", "ARAM"),
            }.Select(entry => YouTubeLibraryRecord.FromEntry(entry, uploadedAt: null)),
            new YouTubePlaylistSettings { Patch = false },
            "2.57.0.98304"
        );

        Assert.Equal(
            new[] { "Cursed Hollow", "Quick Match" },
            items.Select(item => item.PlaylistTitle)
        );
        Assert.All(items, item => Assert.Equal("vid-1", item.VideoId));
        Assert.All(items, item => Assert.Equal(3, item.ReplayId));
    }

    [Fact]
    public void Record_FromAnInsertedEntry_KeepsTheKindAndEnglishMap()
    {
        string path = YouTubeLibraryRecord.PathFor(directory);
        YouTubeLibraryRecord.Append(
            path,
            YouTubeLibraryRecord.FromEntry(
                new YouTubeEntry
                {
                    VideoId = "full-1",
                    ReplayId = 65550001,
                    Map = "용의 둥지",
                    GameType = "Storm League",
                    Rank = "Diamond 2",
                    GameVersion = "2.57.0.98304",
                    PrivacyStatus = "private",
                    ActualPrivacyStatus = "public",
                    DescriptionLines = new[]
                    {
                        "Twitch: https://twitch.tv/saltysadism",
                        "Featured: Illidan",
                        "Draft: Blue double tank",
                    },
                },
                Noon
            )
        );
        YouTubeLibraryRecord.Append(
            path,
            YouTubeLibraryRecord.FromEntry(
                new YouTubeEntry
                {
                    VideoId = "clip-1",
                    ReplayId = 65550001,
                    Map = "Dragon Shire",
                    DescriptionLines = new[] { "clip:65550001:pentakill:Li-Ming" },
                },
                Noon
            )
        );

        Dictionary<string, YouTubeLibraryVideo> record = YouTubeLibraryRecord.Read(path);

        YouTubeLibraryVideo full = record["full-1"];
        Assert.Equal(YouTubeLibraryRecord.Full, full.Kind);
        Assert.Equal("Dragon Shire", full.Map);
        Assert.Equal("Storm League", full.Mode);
        Assert.Equal("Diamond 2", full.Rank);
        Assert.Equal("2.57.0.98304", full.GameVersion);
        Assert.Equal("public", full.PrivacyStatus);
        Assert.Equal(Noon, full.UploadedAt);
        Assert.True(full.IsResolved);
        Assert.Equal("Blue double tank", full.Draft);
        Assert.Equal("Illidan", full.FocusHero);
        Assert.True(full.IsViewerReview);
        Assert.Equal(YouTubeLibraryRecord.Clip, record["clip-1"].Kind);
        Assert.False(record["clip-1"].IsResolved);
        Assert.Null(record["clip-1"].Draft);
        Assert.Null(record["clip-1"].FocusHero);
        string[] lines = File.ReadAllLines(path);
        Assert.DoesNotContain("IsResolved", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("IsViewerReview", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("\"Draft\"", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain("\"FocusHero\"", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Record_SurvivesRetentionThatDeletesTheContext()
    {
        string context = Path.Combine(directory, "Contexts", "65550001");
        Directory.CreateDirectory(context);
        File.WriteAllText(Path.Combine(context, "youtube-entry-uploaded.json"), "{}");
        Directory.SetLastWriteTimeUtc(context, Noon.UtcDateTime.AddDays(-30));
        Directory.CreateDirectory(Path.Combine(directory, "Contexts", "99999999"));
        AppendRecord("video-1", "Sky Temple", "2.57.0.98304");

        MediaRetention.Sweep(
            new AppSettings { Location = new LocationSettings { DataDirectory = directory } },
            Noon
        );

        Assert.False(Directory.Exists(context));
        Assert.Contains(
            "video-1",
            YouTubeLibraryRecord.Read(YouTubeLibraryRecord.PathFor(directory)).Keys
        );
    }

    [Fact]
    public async Task RunOnce_FilesTheRecordAfterTheContextIsGone()
    {
        AppendRecord("video-9", "Cursed Hollow", "2.57.0.98304");
        var client = new FakeClient();

        YouTubeLibraryPass pass = await Library(client).RunOnceAsync(true, CancellationToken.None);

        Assert.Equal(
            new[] { "Cursed Hollow", "Storm League", "Storm League - Platinum", "Season 2026" },
            client.Created
        );
        Assert.Equal(
            new[]
            {
                ("pl-0", "video-9"),
                ("pl-1", "video-9"),
                ("pl-2", "video-9"),
                ("pl-3", "video-9"),
            },
            client.Inserted
        );
        Assert.Equal(4, pass.Filed);
        // One playlists listing, four creates, four inserts.
        Assert.Equal(1 + 4 * 50 + 4 * 50 + UploadsListing(client), pass.UnitsSpent);
    }

    [Fact]
    public async Task RunOnce_FilesOnlyTheGroupsThatAreOn()
    {
        AppendRecord("video-9", "Cursed Hollow", "2.57.0.98304");
        var client = new FakeClient();
        AppSettings settings = Settings(false);
        settings.YouTube.Playlists = new YouTubePlaylistSettings
        {
            Map = false,
            Mode = false,
            Rank = true,
            Draft = false,
            ViewerReview = false,
            Patch = false,
            MapMode = true,
        };

        YouTubeLibraryPass pass = await Library(client, settings: settings)
            .RunOnceAsync(true, CancellationToken.None);

        Assert.Equal(
            new[] { "Storm League - Platinum", "Cursed Hollow - Storm League - Platinum" },
            client.Created
        );
        Assert.Equal(2, pass.Filed);
    }

    [Fact]
    public async Task RunOnce_DedupesTheRecordAndTheContextEntryByVideoId()
    {
        AppendRecord("video-7", "Dragon Shire", "2.57.0.98304");
        WriteEntry(PublicEntry(7, "video-7", "Garden of Terror", "ARAM"));
        WriteEntry(PublicEntry(8, "video-8", "Garden of Terror", "ARAM"), "8");
        var client = new FakeClient();

        YouTubeLibraryPass pass = await Library(client).RunOnceAsync(true, CancellationToken.None);

        Assert.DoesNotContain(
            pass.Planned,
            item => item.VideoId == "video-7" && item.PlaylistTitle is "Garden of Terror" or "ARAM"
        );
        Assert.Contains(
            pass.Planned,
            item => item.PlaylistTitle == "Dragon Shire" && item.VideoId == "video-7"
        );
        Assert.Contains(
            pass.Planned,
            item => item.PlaylistTitle == "Garden of Terror" && item.VideoId == "video-8"
        );
        Assert.Contains(
            pass.Planned,
            item => item.PlaylistTitle == "ARAM" && item.VideoId == "video-8"
        );
        Assert.Equal(
            pass.Planned.Count,
            pass.Planned.Select(item => item.PlaylistTitle + "\n" + item.VideoId).Distinct().Count()
        );
    }

    [Fact]
    public async Task RunOnce_FilesAVideoOnceAndUsesExistingPlaylists()
    {
        AppendRecord("video-7", "Dragon Shire", "2.57.0.98304");
        var client = new FakeClient();
        client.Existing.Add(new YouTubePlaylist("existing-map", "Dragon Shire"));
        client.Existing.Add(new YouTubePlaylist("existing-sl", "Storm League"));
        YouTubeLibrary library = Library(client);

        await library.RunOnceAsync(true, CancellationToken.None);
        YouTubeLibraryPass second = await library.RunOnceAsync(true, CancellationToken.None);

        Assert.Equal(new[] { "Storm League - Platinum", "Season 2026" }, client.Created);
        Assert.Contains(("existing-map", "video-7"), client.Inserted);
        Assert.Contains(("existing-sl", "video-7"), client.Inserted);
        Assert.Equal(4, client.Inserted.Count);
        Assert.Empty(second.Planned);
    }

    [Fact]
    public async Task RunOnce_DiscoversOldTemplateVideosAndResolvesTheBuildFromHeroesProfile()
    {
        var client = new FakeClient();
        client.Pages.Add(
            Page(
                Video(
                    "v-old",
                    "Sky Temple - 65269475 - Platinum",
                    string.Format(OldDescription, 65269475)
                ),
                Video(
                    "v-pl",
                    "Wieże Zagłady - 65537358 - Storm League - Master",
                    string.Format(OldDescription, 65537358)
                        .Replace("Rank: Platinum 1", "Rank: Master")
                )
            )
        );
        var profile = new FakeHeroesProfile();
        profile.Replays[65269475] = HpReplay(65269475, "Sky Temple", "2.55.14.95918");
        profile.Replays[65537358] = HpReplay(65537358, "Towers of Doom", "2.57.0.98304");

        YouTubeLibraryPass pass = await Library(client, profile)
            .RunOnceAsync(true, CancellationToken.None);

        Dictionary<string, YouTubeLibraryVideo> record = YouTubeLibraryRecord.Read(
            YouTubeLibraryRecord.PathFor(directory)
        );
        Assert.Equal("Sky Temple", record["v-old"].Map);
        Assert.Equal("Storm League", record["v-old"].Mode);
        Assert.Equal("Platinum 1", record["v-old"].Rank);
        Assert.Equal("2.55.14.95918", record["v-old"].GameVersion);
        Assert.Equal(65269475, record["v-old"].ReplayId);
        Assert.Equal("Towers of Doom", record["v-pl"].Map);
        Assert.Equal("Master", record["v-pl"].Rank);
        Assert.Equal(2, pass.Recorded);
        Assert.Equal(new[] { 65269475, 65537358 }, profile.Lookups);
        Assert.Empty(profile.Enriched);
        Assert.True(
            YouTubeReplayCatalog.Contains(YouTubeReplayCatalog.PathFor(directory), 65269475)
        );
        Assert.Null(record["v-old"].Draft);
        Assert.Null(record["v-old"].FocusHero);
        Assert.Contains("Sky Temple", client.Created);
        Assert.Contains("Storm League - Platinum", client.Created);
        Assert.Contains("Patch 2.55 archive", client.Created);
        Assert.Contains("Towers of Doom", client.Created);
        Assert.Contains("Storm League - Master", client.Created);
        Assert.DoesNotContain(client.Created, title => title.StartsWith("Unusual drafts"));
        Assert.DoesNotContain(YouTubePlaylistNames.ViewerReviews, client.Created);
    }

    [Fact]
    public async Task RunOnce_CurrentTemplateDraftAndNamedPlayerAreFiled()
    {
        var client = new FakeClient();
        client.Pages.Add(
            Page(
                Video(
                    "v-review",
                    "Illidan focus - Dragon Shire - Storm League - Diamond 3 - Blue no tank, Red double healer - 65600003",
                    "Full match.\nReplay ID: 65600003\nBuild: 2.57.0.98304\nMap: Dragon Shire\nMode: Storm League\nRank: Diamond 3\nFeatured: Illidan\nDraft: Blue no tank, Red double healer"
                )
            )
        );

        YouTubeLibraryPass pass = await Library(client).RunOnceAsync(true, CancellationToken.None);

        YouTubeLibraryVideo video = YouTubeLibraryRecord.Read(
            YouTubeLibraryRecord.PathFor(directory)
        )["v-review"];
        Assert.Equal("Blue no tank, Red double healer", video.Draft);
        Assert.Equal("Illidan", video.FocusHero);
        Assert.Equal(
            new[]
            {
                "Dragon Shire",
                "Storm League",
                "Storm League - Diamond",
                "Unusual drafts - No tank",
                "Unusual drafts - Double healer",
                YouTubePlaylistNames.ViewerReviews,
                "Season 2026",
            },
            pass.Planned.Select(item => item.PlaylistTitle)
        );
        Assert.Equal(7, pass.Filed);
    }

    [Fact]
    public async Task RunOnce_ListsEveryPageOnceSoKnownVideosLearnTheNewFacts()
    {
        AppendRecord("v-2", "Cursed Hollow", "2.57.0.98304");
        new YouTubeUploadsIndex
        {
            PlaylistId = "UU-uploads",
            ListedToEnd = true,
            VideoIds = { "v-1", "v-2" },
            LastRunAt = Noon.AddDays(-1),
        }.Save(YouTubeUploadsIndex.PathFor(directory));
        var client = new FakeClient();
        client.Pages.Add(
            Page(
                Video(
                    "v-1",
                    "Sky Temple - Storm League - Gold - 1",
                    "Replay ID: 1\nBuild: 2.57.0.98304\nMap: Sky Temple\nMode: Storm League\nRank: Gold"
                )
            )
        );
        client.Pages.Add(
            Page(
                Video(
                    "v-2",
                    "Cursed Hollow - Storm League - Platinum - Double healer - 2",
                    "Replay ID: 2\nBuild: 2.57.0.98304\nFeatured: Uther\nDraft: Double healer"
                )
            )
        );
        YouTubeLibrary library = Library(client);

        YouTubeLibraryPass first = await library.RunOnceAsync(true, CancellationToken.None);
        client.PageCalls = 0;
        await library.RunOnceAsync(true, CancellationToken.None);

        YouTubeLibraryVideo known = YouTubeLibraryRecord.Read(
            YouTubeLibraryRecord.PathFor(directory)
        )["v-2"];
        Assert.Equal("Double healer", known.Draft);
        Assert.Equal("Uther", known.FocusHero);
        Assert.Contains(
            first.Planned,
            item => item.PlaylistTitle == "Unusual drafts - Double healer"
        );
        Assert.Contains(
            first.Planned,
            item => item.PlaylistTitle == YouTubePlaylistNames.ViewerReviews
        );
        Assert.Equal(
            YouTubeVideoFacts.Version,
            YouTubeUploadsIndex.Load(YouTubeUploadsIndex.PathFor(directory)).FactsVersion
        );
        Assert.Equal(1, client.PageCalls);
    }

    [Fact]
    public async Task RunOnce_CurrentTemplateNeedsNoHeroesProfile()
    {
        var client = new FakeClient();
        client.Pages.Add(
            Page(
                Video(
                    "v-new",
                    "Volskaya Foundry - Storm League - Platinum - 65600001",
                    "Full match.\nReplay ID: 65600001\nBuild: 2.57.0.98304\nMap: Volskaya Foundry\nMode: Storm League\nRank: Platinum 2"
                )
            )
        );
        var profile = new FakeHeroesProfile();

        await Library(client, profile).RunOnceAsync(true, CancellationToken.None);

        Assert.Empty(profile.Lookups);
        YouTubeLibraryVideo video = YouTubeLibraryRecord.Read(
            YouTubeLibraryRecord.PathFor(directory)
        )["v-new"];
        Assert.Equal("Platinum 2", video.Rank);
        Assert.Equal("2.57.0.98304", video.GameVersion);
    }

    [Fact]
    public async Task RunOnce_MissingRankIsEnrichedFromHeroesProfile()
    {
        var client = new FakeClient();
        client.Pages.Add(
            Page(Video("v-1", "Cursed Hollow - Storm League - 65600002", "Replay ID: 65600002"))
        );
        var profile = new FakeHeroesProfile { EnrichedRank = "Diamond 4" };
        profile.Replays[65600002] = HpReplay(65600002, "Cursed Hollow", "2.57.0.98304");

        await Library(client, profile).RunOnceAsync(true, CancellationToken.None);

        Assert.Equal(new[] { 65600002 }, profile.Enriched);
        Assert.Equal(
            "Diamond 4",
            YouTubeLibraryRecord.Read(YouTubeLibraryRecord.PathFor(directory))["v-1"].Rank
        );
    }

    [Fact]
    public async Task RunOnce_UnresolvedVideoWaitsBeforeItIsLookedUpAgain()
    {
        var client = new FakeClient();
        client.Pages.Add(
            Page(
                Video(
                    "v-1",
                    "Sky Temple - 65269475 - Platinum",
                    string.Format(OldDescription, 65269475)
                )
            )
        );
        var profile = new FakeHeroesProfile();
        YouTubeLibrary library = Library(client, profile);

        YouTubeLibraryPass first = await library.RunOnceAsync(true, CancellationToken.None);
        await library.RunOnceAsync(true, CancellationToken.None);
        library.Clock = () => Noon.AddHours(5);
        await library.RunOnceAsync(true, CancellationToken.None);

        Assert.Equal(1, first.Unresolved);
        Assert.Single(profile.Lookups);
        YouTubeUnresolvedVideo waiting = Assert.Single(
            YouTubeUploadsIndex.Load(YouTubeUploadsIndex.PathFor(directory)).Unresolved
        );
        Assert.Equal(1, waiting.Attempts);
        Assert.Equal(Noon.AddHours(6), waiting.NextAttemptAt);
        Assert.Empty(YouTubeLibraryRecord.Read(YouTubeLibraryRecord.PathFor(directory)));

        profile.Replays[65269475] = HpReplay(65269475, "Sky Temple", "2.55.14.95918");
        library.Clock = () => Noon.AddHours(6);
        YouTubeLibraryPass resolved = await library.RunOnceAsync(true, CancellationToken.None);

        Assert.Equal(2, profile.Lookups.Count);
        Assert.Equal(0, resolved.Unresolved);
        Assert.Contains(
            "v-1",
            YouTubeLibraryRecord.Read(YouTubeLibraryRecord.PathFor(directory)).Keys
        );
    }

    [Fact]
    public async Task RunOnce_AnInsertedClipGetsItsBuildFromHeroesProfile()
    {
        YouTubeLibraryRecord.Append(
            YouTubeLibraryRecord.PathFor(directory),
            new YouTubeLibraryVideo
            {
                VideoId = "clip-1",
                ReplayId = 65550001,
                Kind = YouTubeLibraryRecord.Clip,
                Map = "Alterac Pass",
                PrivacyStatus = "public",
            }
        );
        var client = new FakeClient();
        var profile = new FakeHeroesProfile();
        profile.Replays[65550001] = HpReplay(65550001, "Alterac Pass", "2.57.0.98304");

        YouTubeLibraryPass pass = await Library(client, profile)
            .RunOnceAsync(true, CancellationToken.None);

        Assert.Equal(
            "2.57.0.98304",
            YouTubeLibraryRecord.Read(YouTubeLibraryRecord.PathFor(directory))["clip-1"].GameVersion
        );
        YouTubeLibraryItem item = Assert.Single(pass.Planned);
        Assert.Equal("Season 2026", item.PlaylistTitle);
    }

    [Fact]
    public async Task RunOnce_StopsListingAtAPageOfKnownVideosOnceTheChannelWasListedToTheEnd()
    {
        var client = new FakeClient();
        client.Pages.Add(
            Page(
                Video(
                    "v-3",
                    "Cursed Hollow - Storm League - Gold - 3",
                    "Replay ID: 3\nBuild: 2.57.0.98304"
                )
            )
        );
        client.Pages.Add(
            Page(
                Video(
                    "v-2",
                    "Cursed Hollow - Storm League - Gold - 2",
                    "Replay ID: 2\nBuild: 2.57.0.98304"
                )
            )
        );
        client.Pages.Add(
            Page(
                Video(
                    "v-1",
                    "Cursed Hollow - Storm League - Gold - 1",
                    "Replay ID: 1\nBuild: 2.57.0.98304"
                )
            )
        );
        YouTubeLibrary library = Library(client);

        YouTubeLibraryPass first = await library.RunOnceAsync(true, CancellationToken.None);
        client.PageCalls = 0;
        client.Pages.Insert(
            0,
            Page(
                Video(
                    "v-4",
                    "Cursed Hollow - Storm League - Gold - 4",
                    "Replay ID: 4\nBuild: 2.57.0.98304"
                )
            )
        );
        YouTubeLibraryPass second = await library.RunOnceAsync(true, CancellationToken.None);

        Assert.Equal(3, first.NewVideos);
        Assert.Equal(1, second.NewVideos);
        Assert.Equal(2, client.PageCalls);
        Assert.Equal(1, client.ChannelCalls);
        Assert.True(YouTubeReplayCatalog.Contains(YouTubeReplayCatalog.PathFor(directory), 4));
    }

    [Fact]
    public async Task RunOnce_ScheduledVideoPastItsPublishTimeIsCheckedByIdAndFiled()
    {
        YouTubeLibraryRecord.Append(
            YouTubeLibraryRecord.PathFor(directory),
            new YouTubeLibraryVideo
            {
                VideoId = "v-old-sched",
                ReplayId = 6,
                Kind = YouTubeLibraryRecord.Full,
                Map = "Sky Temple",
                Mode = "Quick Match",
                GameVersion = "2.57.0.98304",
                PrivacyStatus = "private",
                PublishAt = Noon.AddHours(-2),
            }
        );
        var client = new FakeClient();
        client.Privacy["v-old-sched"] = "public";
        YouTubeLibrary library = Library(client);

        YouTubeLibraryPass pass = await library.RunOnceAsync(true, CancellationToken.None);

        Assert.Equal(new[] { "v-old-sched" }, Assert.Single(client.PrivacyCalls));
        Assert.Contains(pass.Planned, item => item.PlaylistTitle == "Sky Temple");
        Assert.Equal(
            "public",
            YouTubeLibraryRecord
                .Read(YouTubeLibraryRecord.PathFor(directory))["v-old-sched"]
                .PrivacyStatus
        );
    }

    /// <summary>
    /// #250: production logged "9600 of 10000 units used today" after six uploads, so the
    /// pass had no pool room and never confirmed a scheduled video public. Google counted
    /// those uploads in the Video Uploads bucket (6 of 100) and 4 queries in the pool.
    /// </summary>
    [Fact]
    public async Task RunOnce_ConfirmsScheduledVideosAfterADayOfSixUploadsFromAnOldLedger()
    {
        File.WriteAllText(
            Path.Combine(directory, YouTubeQuotaUnits.FileName),
            JsonSerializer.Serialize(
                new
                {
                    QuotaDay = PublicationSchedule.QuotaDayStart(Noon),
                    UploadUnits = 6 * 1600,
                    LibraryUnits = 0,
                }
            )
        );
        YouTubeLibraryRecord.Append(
            YouTubeLibraryRecord.PathFor(directory),
            new YouTubeLibraryVideo
            {
                VideoId = "v-due",
                ReplayId = 6,
                Kind = YouTubeLibraryRecord.Full,
                Map = "Sky Temple",
                Mode = "Quick Match",
                GameVersion = "2.57.0.98304",
                PrivacyStatus = "private",
                PublishAt = Noon.AddHours(-1),
            }
        );
        var client = new FakeClient();
        client.Privacy["v-due"] = "public";
        AppSettings settings = Settings(false);
        settings.YouTube.DailyQuotaUnits = 10000;
        settings.YouTube.QuotaReserveUnits = 1600;

        YouTubeLibraryPass pass = await Library(client, settings: settings)
            .RunOnceAsync(true, CancellationToken.None);

        Assert.Single(client.PrivacyCalls);
        Assert.True(pass.UnitsSpent > 0);
        Assert.Equal(
            "public",
            YouTubeLibraryRecord
                .Read(YouTubeLibraryRecord.PathFor(directory))["v-due"]
                .PrivacyStatus
        );
        YouTubeQuotaDay day = new YouTubeQuotaUnits(directory, settings.YouTube).Read(Noon);
        Assert.Equal(6, day.UploadCalls);
        Assert.Equal(0, day.UploadUnits);
        Assert.Equal(pass.UnitsSpent, day.Total);
    }

    [Fact]
    public async Task RunOnce_ScheduledVideoNotYetDueIsNotLookedUp()
    {
        YouTubeLibraryRecord.Append(
            YouTubeLibraryRecord.PathFor(directory),
            new YouTubeLibraryVideo
            {
                VideoId = "v-future",
                ReplayId = 7,
                Kind = YouTubeLibraryRecord.Full,
                Map = "Sky Temple",
                Mode = "Quick Match",
                GameVersion = "2.57.0.98304",
                PrivacyStatus = "private",
                PublishAt = Noon.AddHours(3),
            }
        );
        var client = new FakeClient();
        YouTubeLibrary library = Library(client);

        YouTubeLibraryPass pass = await library.RunOnceAsync(true, CancellationToken.None);

        Assert.Empty(client.PrivacyCalls);
        Assert.Empty(pass.Planned);
    }

    [Fact]
    public async Task RunOnce_PrivateUploadIsFiledOnceTheListingSeesItPublic()
    {
        YouTubeLibraryRecord.Append(
            YouTubeLibraryRecord.PathFor(directory),
            new YouTubeLibraryVideo
            {
                VideoId = "v-sched",
                ReplayId = 5,
                Kind = YouTubeLibraryRecord.Full,
                Map = "Sky Temple",
                Mode = "Quick Match",
                GameVersion = "2.57.0.98304",
                PrivacyStatus = "private",
            }
        );
        var client = new FakeClient();
        YouTubeLibrary library = Library(client);

        YouTubeLibraryPass hidden = await library.RunOnceAsync(true, CancellationToken.None);
        client.Pages.Add(
            Page(Video("v-sched", "Sky Temple - Quick Match - 5", "Replay ID: 5", "public"))
        );
        YouTubeLibraryPass shown = await library.RunOnceAsync(true, CancellationToken.None);

        Assert.Empty(hidden.Planned);
        Assert.Contains(shown.Planned, item => item.PlaylistTitle == "Sky Temple");
        Assert.Contains(shown.Planned, item => item.PlaylistTitle == "Quick Match");
    }

    [Fact]
    public async Task RunOnce_RunsOncePerIntervalAcrossARestart()
    {
        var client = new FakeClient();

        YouTubeLibraryPass first = await Library(client)
            .RunOnceAsync(false, CancellationToken.None);
        YouTubeLibrary restarted = Library(client);
        restarted.Clock = () => Noon.AddMinutes(59);
        YouTubeLibraryPass early = await restarted.RunOnceAsync(false, CancellationToken.None);
        restarted.Clock = () => Noon.AddHours(1);
        YouTubeLibraryPass due = await restarted.RunOnceAsync(false, CancellationToken.None);

        Assert.Null(first.Skipped);
        Assert.NotNull(early.Skipped);
        Assert.Null(due.Skipped);
        Assert.Equal(2, client.PageCalls);
    }

    [Fact]
    public async Task RunOnce_QuotaErrorPausesThePassUntilTheNextPacificDay()
    {
        var client = new FakeClient
        {
            UploadsError = new InvalidOperationException("quotaExceeded"),
        };
        YouTubeLibrary library = Library(client);

        YouTubeLibraryPass failed = await library.RunOnceAsync(true, CancellationToken.None);
        int calls = client.Calls;
        YouTubeLibraryPass paused = await library.RunOnceAsync(true, CancellationToken.None);
        Assert.Equal(calls, client.Calls);
        library.Clock = () => YouTubeListQuota.ResumeAt(Noon);
        client.UploadsError = null;
        YouTubeLibraryPass resumed = await library.RunOnceAsync(true, CancellationToken.None);

        Assert.Null(failed.Skipped);
        Assert.Contains("paused", paused.Skipped, StringComparison.Ordinal);
        Assert.Null(resumed.Skipped);
        YouTubeQuotaDay day = new YouTubeQuotaUnits(directory, Settings(false).YouTube).Read(Noon);
        Assert.Equal(YouTubeListQuota.ResumeAt(Noon), day.LibraryPausedUntil);
    }

    [Fact]
    public async Task RunOnce_RateLimitEndsThePassWithoutPausingTheDay()
    {
        AppendRecord("video-7", "Dragon Shire", "2.57.0.98304");
        var client = new FakeClient
        {
            InsertError = new InvalidOperationException(
                "The service youtube has thrown an exception. HttpStatusCode is TooManyRequests. Quota exceeded for quota metric 'Queries' and limit 'Queries per minute' of service 'youtube.googleapis.com'. [rateLimitExceeded]"
            ),
        };
        YouTubeLibrary library = Library(client);

        YouTubeLibraryPass throttled = await library.RunOnceAsync(true, CancellationToken.None);

        // One refused write ends the pass: no retries into the throttle.
        Assert.Null(throttled.Skipped);
        Assert.Equal(1, client.InsertAttempts);
        YouTubeQuotaDay day = new YouTubeQuotaUnits(directory, Settings(false).YouTube).Read(Noon);
        Assert.Null(day.LibraryPausedUntil);

        // The next pass, an hour on, is not held until the Pacific day turns.
        client.InsertError = null;
        library.Clock = () => Noon.AddHours(1);
        YouTubeLibraryPass next = await library.RunOnceAsync(false, CancellationToken.None);

        Assert.Null(next.Skipped);
        Assert.Contains(client.Inserted, insert => insert.VideoId == "video-7");
    }

    [Fact]
    public async Task RunOnce_SpacesPlaylistWritesButNotTheFirst()
    {
        AppendRecord("video-7", "Dragon Shire", "2.57.0.98304");
        var client = new FakeClient();
        client.Existing.Add(new YouTubePlaylist("existing-map", "Dragon Shire"));
        YouTubeLibrary library = Library(client);

        await library.RunOnceAsync(true, CancellationToken.None);

        int writes = client.Created.Count + client.Inserted.Count;
        Assert.True(writes > 1);
        Assert.Equal(Enumerable.Repeat(TimeSpan.FromSeconds(5), writes - 1), delays);
    }

    [Fact]
    public async Task RunOnce_ThrottledCreateStillFilesExistingPlaylistsAndLeavesTheRestPlanned()
    {
        AppendRecord("video-1", "Sky Temple", "2.57.0.98304");
        AppendRecord("video-2", "Dragon Shire", "2.57.0.98304");
        var client = new FakeClient { CreateError = PlaylistCreateThrottle() };
        client.Existing.Add(new YouTubePlaylist("existing-sky", "Sky Temple"));
        client.Existing.Add(new YouTubePlaylist("existing-sl", "Storm League"));
        YouTubeLibrary library = Library(client);

        YouTubeLibraryPass throttled = await library.RunOnceAsync(true, CancellationToken.None);

        // Every item whose playlist exists is filed; the first refused create ends the creates.
        Assert.Null(throttled.Skipped);
        Assert.Equal(8, throttled.Planned.Count);
        Assert.Equal(3, throttled.Filed);
        Assert.Equal(
            new[]
            {
                ("existing-sky", "video-1"),
                ("existing-sl", "video-1"),
                ("existing-sl", "video-2"),
            },
            client.Inserted
        );
        Assert.Equal(1, client.CreateAttempts);
        Assert.Empty(client.Created);
        YouTubePlaylistCache cache = ReadCache();
        Assert.Equal(
            new[] { "Sky Temple", "Storm League" },
            cache.PlaylistIds.Keys.OrderBy(title => title, StringComparer.Ordinal)
        );
        // A playlist throttle is not the day's quota.
        Assert.Null(
            new YouTubeQuotaUnits(directory, Settings(false).YouTube).Read(Noon).LibraryPausedUntil
        );

        // The waiting items stay planned, and the next pass creates their playlists.
        client.CreateError = null;
        library.Clock = () => Noon.AddHours(1);
        YouTubeLibraryPass next = await library.RunOnceAsync(false, CancellationToken.None);

        Assert.Null(next.Skipped);
        Assert.Equal(5, next.Planned.Count);
        Assert.Equal(5, next.Filed);
        Assert.Equal(
            new[] { "Storm League - Platinum", "Season 2026", "Dragon Shire" },
            client.Created
        );
        Assert.Equal(8, client.Inserted.Distinct().Count());
    }

    [Fact]
    public async Task RunOnce_CreatesAtMostTheCapPerPassAfterFilingExistingPlaylists()
    {
        Assert.Equal(3, new YouTubeSettings().LibraryMaxNewPlaylistsPerPass);
        AppendRecord("video-1", "Sky Temple", "2.57.0.98304");
        AppendRecord("video-2", "Dragon Shire", "2.57.0.98304");
        var client = new FakeClient();
        client.Existing.Add(new YouTubePlaylist("existing-sky", "Sky Temple"));
        AppSettings settings = Settings(false);
        settings.YouTube.LibraryMaxNewPlaylistsPerPass = 2;
        YouTubeLibrary library = Library(client, settings: settings);

        YouTubeLibraryPass first = await library.RunOnceAsync(true, CancellationToken.None);

        // The existing playlist is filed before any create. The playlists two videos wait
        // for come before the map playlist only one waits for.
        Assert.Equal(("existing-sky", "video-1"), client.Inserted[0]);
        Assert.Equal(new[] { "Storm League", "Storm League - Platinum" }, client.Created);
        Assert.Equal(5, first.Filed);
        Assert.Equal(8, first.Planned.Count);

        YouTubeLibraryPass second = await library.RunOnceAsync(true, CancellationToken.None);

        Assert.Equal(
            new[] { "Storm League", "Storm League - Platinum", "Season 2026", "Dragon Shire" },
            client.Created
        );
        Assert.Equal(3, second.Filed);

        YouTubeLibraryPass third = await library.RunOnceAsync(true, CancellationToken.None);

        Assert.Empty(third.Planned);
        Assert.Equal(4, client.CreateAttempts);
    }

    [Fact]
    public async Task RunOnce_CapOfZeroOnlyFilesExistingPlaylists()
    {
        AppendRecord("video-7", "Dragon Shire", "2.57.0.98304");
        var client = new FakeClient();
        client.Existing.Add(new YouTubePlaylist("existing-map", "Dragon Shire"));
        AppSettings settings = Settings(false);
        settings.YouTube.LibraryMaxNewPlaylistsPerPass = 0;

        YouTubeLibraryPass pass = await Library(client, settings: settings)
            .RunOnceAsync(true, CancellationToken.None);

        Assert.Equal(0, client.CreateAttempts);
        Assert.Equal(new[] { ("existing-map", "video-7") }, client.Inserted);
        Assert.Equal(1, pass.Filed);
    }

    [Fact]
    public async Task RunOnce_ThrottledInsertStillEndsThePassBeforeAnyCreate()
    {
        AppendRecord("video-7", "Dragon Shire", "2.57.0.98304");
        var client = new FakeClient
        {
            InsertError = new InvalidOperationException(
                "The service youtube has thrown an exception. HttpStatusCode is TooManyRequests. [rateLimitExceeded]"
            ),
        };
        client.Existing.Add(new YouTubePlaylist("existing-map", "Dragon Shire"));

        YouTubeLibraryPass pass = await Library(client).RunOnceAsync(true, CancellationToken.None);

        Assert.Null(pass.Skipped);
        Assert.Equal(1, client.InsertAttempts);
        Assert.Equal(0, client.CreateAttempts);
        Assert.Equal(0, pass.Filed);
        Assert.Null(
            new YouTubeQuotaUnits(directory, Settings(false).YouTube).Read(Noon).LibraryPausedUntil
        );
    }

    [Fact]
    public async Task RunOnce_PlaylistOnTheChannelButNotInTheCacheIsNotCreatedAgain()
    {
        // A manual run created these playlists, but its cache write was lost.
        File.WriteAllText(
            Path.Combine(directory, YouTubeLibrary.CacheFileName),
            JsonSerializer.Serialize(
                new YouTubePlaylistCache
                {
                    PlaylistIds = new Dictionary<string, string>
                    {
                        ["Dragon Shire"] = "cached-map",
                    },
                }
            )
        );
        AppendRecord("video-7", "Dragon Shire", "2.57.0.98304");
        var client = new FakeClient();
        client.Existing.Add(new YouTubePlaylist("yt-sl", "Storm League"));
        // YouTube Studio may keep a title with other case or spacing.
        client.Existing.Add(new YouTubePlaylist("yt-rank", "storm league -  Platinum "));
        client.Existing.Add(new YouTubePlaylist("yt-season", "Season 2026"));
        YouTubeLibrary library = Library(client);

        YouTubeLibraryPass pass = await library.RunOnceAsync(true, CancellationToken.None);

        Assert.Equal(0, client.CreateAttempts);
        Assert.Equal(1, client.PlaylistListCalls);
        Assert.Equal(
            new[]
            {
                ("cached-map", "video-7"),
                ("yt-sl", "video-7"),
                ("yt-rank", "video-7"),
                ("yt-season", "video-7"),
            },
            client.Inserted
        );
        Assert.Equal(4, pass.Filed);
        Assert.Equal("yt-rank", ReadCache().PlaylistIds["Storm League - Platinum"]);

        // Every title is cached now: the next pass neither lists nor creates.
        YouTubeLibraryPass next = await library.RunOnceAsync(true, CancellationToken.None);

        Assert.Empty(next.Planned);
        Assert.Equal(1, client.PlaylistListCalls);
        Assert.Equal(0, client.CreateAttempts);
    }

    [Fact]
    public async Task RunOnce_StopsWhenTheDaysLibraryUnitsAreSpent()
    {
        AppendRecord("video-1", "Sky Temple", "2.57.0.98304");
        AppendRecord("video-2", "Dragon Shire", "2.57.0.98304");
        var client = new FakeClient();
        AppSettings settings = Settings(false);
        settings.YouTube.LibraryUnitsPerDay = 160;

        YouTubeLibraryPass pass = await Library(client, settings: settings)
            .RunOnceAsync(true, CancellationToken.None);

        // 2 listing units, 1 playlists page, 3 creates... only 160 units fit.
        Assert.True(pass.UnitsSpent <= 160);
        Assert.True(pass.Filed < pass.Planned.Count);
        Assert.Equal(
            pass.UnitsSpent,
            new YouTubeQuotaUnits(directory, settings.YouTube).Read(Noon).LibraryUnits
        );
    }

    [Fact]
    public async Task RunOnce_DryRunWritesThePlanAndCallsNothing()
    {
        AppendRecord("video-42", "Sky Temple", "2.57.0.98304");
        new YouTubeUploadsIndex
        {
            Unresolved =
            {
                new YouTubeUnresolvedVideo
                {
                    Video = new YouTubeLibraryVideo { VideoId = "v-x", ReplayId = 77 },
                    NextAttemptAt = Noon,
                },
            },
        }.Save(YouTubeUploadsIndex.PathFor(directory));
        var client = new FakeClient();
        var profile = new FakeHeroesProfile();
        YouTubeLibrary library = Library(client, profile, Settings(true));

        YouTubeLibraryPass pass = await library.RunOnceAsync(true, CancellationToken.None);

        Assert.True(pass.DryRun);
        Assert.Equal(0, client.Calls);
        Assert.Empty(profile.Lookups);
        string plan = await File.ReadAllTextAsync(
            Path.Combine(directory, YouTubeLibrary.DryRunFileName)
        );
        Assert.Contains("\"Simulated\": true", plan, StringComparison.Ordinal);
        Assert.Contains("\"Storm League - Platinum\"", plan, StringComparison.Ordinal);
        Assert.Contains("\"Sky Temple\"", plan, StringComparison.Ordinal);
        Assert.Contains("\"InsertUnits\": 200", plan, StringComparison.Ordinal);
        Assert.Contains("\"WouldResolve\"", plan, StringComparison.Ordinal);
        Assert.Contains("v-x", plan, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(directory, YouTubeLibrary.CacheFileName)));
    }

    [Fact]
    public async Task RunOnce_SkipsWhileAnotherProcessHoldsThePass()
    {
        var client = new FakeClient();
        using (
            FileStream held = DurableFile.TryLock(
                Path.Combine(directory, YouTubeLibrary.LockFileName)
            )
        )
        {
            YouTubeLibraryPass skipped = await Library(client)
                .RunOnceAsync(true, CancellationToken.None);

            Assert.Contains("another process", skipped.Skipped, StringComparison.Ordinal);
            Assert.Equal(0, client.Calls);
        }

        YouTubeLibraryPass ran = await Library(client).RunOnceAsync(true, CancellationToken.None);
        Assert.Null(ran.Skipped);
    }

    private YouTubeLibrary Library(
        FakeClient client,
        FakeHeroesProfile profile = null,
        AppSettings settings = null
    ) =>
        new(NullLogger<YouTubeLibrary>.Instance, settings ?? Settings(false), client, profile)
        {
            Clock = () => Noon,
            Delay = (wait, _) =>
            {
                delays.Add(wait);
                return Task.CompletedTask;
            },
        };

    private AppSettings Settings(bool dryRun) =>
        new()
        {
            Location = new LocationSettings { DataDirectory = directory },
            Spectate = new SpectateSettings { MinimumGameVersion = "2.57.0.98304" },
            YouTube = new YouTubeSettings
            {
                DryRun = dryRun,
                EntryFileNameUploaded = "youtube-entry-uploaded.json",
                SeasonName = "Season 2026",
                // Most tests file one video into four new playlists; the cap has its own tests.
                LibraryMaxNewPlaylistsPerPass = 10,
            },
        };

    private static int UploadsListing(FakeClient client) => client.ChannelCalls + client.PageCalls;

    /// <summary>The playlists.insert refusal production logged on 2026-10-08.</summary>
    internal static GoogleApiException PlaylistCreateThrottle() =>
        new("youtube", "Resource has been exhausted (e.g. check quota).")
        {
            HttpStatusCode = HttpStatusCode.TooManyRequests,
            Error = new RequestError
            {
                Code = 429,
                Message = "Resource has been exhausted (e.g. check quota).",
                Errors =
                [
                    new SingleError
                    {
                        Reason = "RATE_LIMIT_EXCEEDED",
                        Domain = "youtube.api.v3.PlaylistInsertResponse.Error",
                    },
                ],
            },
        };

    private YouTubePlaylistCache ReadCache() =>
        JsonSerializer.Deserialize<YouTubePlaylistCache>(
            File.ReadAllText(Path.Combine(directory, YouTubeLibrary.CacheFileName))
        );

    private void AppendRecord(string videoId, string map, string version) =>
        YouTubeLibraryRecord.Append(
            YouTubeLibraryRecord.PathFor(directory),
            new YouTubeLibraryVideo
            {
                VideoId = videoId,
                ReplayId = 1,
                Kind = YouTubeLibraryRecord.Full,
                Map = map,
                Mode = "Storm League",
                Rank = "Platinum",
                GameVersion = version,
                PrivacyStatus = "public",
            }
        );

    private static YouTubeEntry PublicEntry(
        int replayId,
        string videoId,
        string map,
        string mode
    ) =>
        new()
        {
            ReplayId = replayId,
            VideoId = videoId,
            Map = map,
            GameType = mode,
            PrivacyStatus = "public",
        };

    private void WriteEntry(YouTubeEntry entry, string folderName = "1")
    {
        string folder = Path.Combine(directory, "Contexts", folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "youtube-entry-uploaded.json"),
            JsonSerializer.Serialize(entry)
        );
    }

    private static HeroesProfileReplay HpReplay(int id, string map, string version) =>
        new()
        {
            Id = id,
            Map = map,
            GameType = "Storm League",
            GameVersion = version,
        };

    private static YouTubeUploadsPage Page(params YouTubeUploadedVideo[] videos) =>
        new() { Videos = videos };

    private static YouTubeUploadedVideo Video(
        string id,
        string title,
        string description,
        string privacy = "public"
    ) =>
        new()
        {
            VideoId = id,
            Title = title,
            Description = description,
            PrivacyStatus = privacy,
        };

    private sealed class FakeClient : IYouTubePlaylistClient
    {
        public List<YouTubeUploadsPage> Pages { get; } = new();
        public List<YouTubePlaylist> Existing { get; } = new();
        public List<string> Created { get; } = new();
        public List<(string PlaylistId, string VideoId)> Inserted { get; } = new();
        public Exception UploadsError { get; set; }
        public int ChannelCalls { get; set; }
        public int PageCalls { get; set; }
        public int Calls { get; private set; }
        public int PlaylistListCalls { get; private set; }
        public bool Consent { get; set; } = true;
        public int ConsentChecks { get; private set; }

        public Task<bool> HasConsentAsync(CancellationToken cancellationToken)
        {
            ConsentChecks++;
            return Task.FromResult(Consent);
        }

        public Task<string> UploadsPlaylistIdAsync(CancellationToken cancellationToken)
        {
            Calls++;
            ChannelCalls++;
            return Task.FromResult("UU-uploads");
        }

        public Task<YouTubeUploadsPage> UploadsAsync(
            string playlistId,
            string pageToken,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            PageCalls++;
            if (UploadsError != null)
            {
                throw UploadsError;
            }

            int index = pageToken == null ? 0 : int.Parse(pageToken);
            if (index >= Pages.Count)
            {
                return Task.FromResult(new YouTubeUploadsPage { Videos = [] });
            }

            return Task.FromResult(
                new YouTubeUploadsPage
                {
                    Videos = Pages[index].Videos,
                    NextPageToken = index + 1 < Pages.Count ? (index + 1).ToString() : null,
                }
            );
        }

        public Task<YouTubePlaylistsPage> PlaylistsAsync(
            string pageToken,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            PlaylistListCalls++;
            return Task.FromResult(new YouTubePlaylistsPage { Playlists = Existing.ToList() });
        }

        public Dictionary<string, string> Privacy { get; } = new();
        public List<IReadOnlyList<string>> PrivacyCalls { get; } = new();

        public Task<IReadOnlyDictionary<string, YouTubeVideoStatus>> StatusAsync(
            IReadOnlyList<string> videoIds,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            PrivacyCalls.Add(videoIds);
            IReadOnlyDictionary<string, YouTubeVideoStatus> found = videoIds
                .Where(Privacy.ContainsKey)
                .ToDictionary(id => id, id => new YouTubeVideoStatus(Privacy[id], "processed"));
            return Task.FromResult(found);
        }

        public Exception CreateError { get; set; }

        public int CreateAttempts { get; private set; }

        public Task<string> CreateAsync(string title, CancellationToken cancellationToken)
        {
            Calls++;
            CreateAttempts++;
            if (CreateError != null)
            {
                throw CreateError;
            }

            Created.Add(title);
            return Task.FromResult("pl-" + (Created.Count - 1));
        }

        public Exception InsertError { get; set; }

        public int InsertAttempts { get; private set; }

        public Task InsertAsync(
            string playlistId,
            string videoId,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            InsertAttempts++;
            if (InsertError != null)
            {
                throw InsertError;
            }

            Inserted.Add((playlistId, videoId));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHeroesProfile : IHeroesProfileService
    {
        public Dictionary<int, HeroesProfileReplay> Replays { get; } = new();
        public List<int> Lookups { get; } = new();
        public List<int> Enriched { get; } = new();
        public string EnrichedRank { get; set; }

        public Task<HeroesProfileReplay> GetReplayByIdAsync(int replayId)
        {
            Lookups.Add(replayId);
            Replays.TryGetValue(replayId, out HeroesProfileReplay replay);
            return Task.FromResult(replay);
        }

        public Task EnrichRankAsync(HeroesProfileReplay replay, CancellationToken cancellationToken)
        {
            Enriched.Add(replay.Id);
            replay.Rank = EnrichedRank;
            return Task.CompletedTask;
        }

        public Task<int> GetMaxReplayIdAsync() => throw new NotSupportedException();

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByFilters(
            GameType? gameType = null,
            GameRank? gameRank = null,
            string gameMap = null
        ) => throw new NotSupportedException();

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByMinId(int minId) =>
            throw new NotSupportedException();

        public Task<ReplayListing> ListPageAsync(int minId) => throw new NotSupportedException();

        public Task<IReadOnlyList<HeroesProfileReplay>> ListAfterAsync(
            int after,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task DownloadReplayAsync(
            int replayId,
            Stream destination,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }
}
