using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Playlists;
using HeroesReplay.Core.YouTube.Publication;
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

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "heroesreplay-library-" + Guid.NewGuid().ToString("N")
    );

    public YouTubeLibraryTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
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
            },
        };

    private static int UploadsListing(FakeClient client) => client.ChannelCalls + client.PageCalls;

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
            return Task.FromResult(new YouTubePlaylistsPage { Playlists = Existing.ToList() });
        }

        public Task<string> CreateAsync(string title, CancellationToken cancellationToken)
        {
            Calls++;
            Created.Add(title);
            return Task.FromResult("pl-" + (Created.Count - 1));
        }

        public Task InsertAsync(
            string playlistId,
            string videoId,
            CancellationToken cancellationToken
        )
        {
            Calls++;
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
