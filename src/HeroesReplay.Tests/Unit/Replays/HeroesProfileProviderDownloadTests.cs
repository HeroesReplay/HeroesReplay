using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.HeroesProfile.Interest;
using HeroesReplay.Core.HeroFocus;
using HeroesReplay.Core.HeroFocus.Targeted;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Kiota.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Replays;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroesProfileProviderDownloadTests
{
    [Fact]
    public async Task DownloadNextAsync_CancelledDownloadDoesNotRetryOrLeaveTheReplay()
    {
        string root = NewRoot();
        using var stop = new CancellationTokenSource();
        var resume = new HeroesProfileResume();
        resume.Arm();
        var service = new ScriptedDownloads(
            async (destination, token) =>
            {
                await destination.WriteAsync(new byte[] { 1, 2, 3 });
                stop.Cancel();
                token.ThrowIfCancellationRequested();
            }
        );

        try
        {
            HeroesProfileProvider provider = Provider(root, service, resume, stop.Token);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                provider.DownloadNextAsync()
            );

            Assert.Equal(1, service.Downloads);
            Assert.True(resume.IsPending);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "Requests")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadNextAsync_RetriesOnceAfterAnOutageAndWritesTheWholeReplay()
    {
        string root = NewRoot();
        var resume = new HeroesProfileResume();
        resume.Arm();
        var service = new ScriptedDownloads(
            async (destination, _) =>
            {
                await destination.WriteAsync(new byte[] { 9, 9 });
            }
        );
        service.FailFirst = true;

        try
        {
            HeroesProfileProvider provider = Provider(
                root,
                service,
                resume,
                CancellationToken.None
            );

            Assert.True(await provider.DownloadNextAsync());

            Assert.Equal(2, service.Downloads);
            Assert.False(resume.IsPending);
            string[] files = Directory.GetFiles(Path.Combine(root, "Requests"));
            string replay = Assert.Single(files);
            Assert.EndsWith(".StormReplay", replay);
            Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(replay));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadNextAsync_JumpsPastAStaleCursorToARecentReplay()
    {
        string root = NewRoot();
        DateTime now = DateTime.UtcNow;
        var service = new ListedDownloads(
            newest: 65660000,
            listPage: minId =>
                minId < 65657000
                    ? Replay(minId + 1, now - TimeSpan.FromDays(3))
                    : Replay(minId + 1, now - TimeSpan.FromHours(1))
        );

        try
        {
            HeroesProfileProvider provider = Provider(
                root,
                service,
                resume: null,
                CancellationToken.None,
                enableRequests: false
            );

            Assert.True(await provider.DownloadNextAsync());

            Assert.Equal(65657001, Assert.Single(service.Downloaded));
            Assert.Equal(1, service.MaxIdLookups);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>#217: a max age of zero turns catch-up off, with no newest-id lookup.</summary>
    [Fact]
    public async Task DownloadNextAsync_ZeroMaxAgeKeepsTheCursorWithoutAMaxIdLookup()
    {
        string root = NewRoot();
        DateTime now = DateTime.UtcNow;
        var service = new ListedDownloads(
            newest: 65660000,
            listPage: minId => Replay(minId + 1, now - TimeSpan.FromDays(3))
        );

        try
        {
            HeroesProfileProvider provider = Provider(
                root,
                service,
                resume: null,
                CancellationToken.None,
                enableRequests: false,
                maxReplayAge: TimeSpan.Zero
            );

            Assert.True(await provider.DownloadNextAsync());

            Assert.Equal(65580001, Assert.Single(service.Downloaded));
            Assert.Equal(0, service.MaxIdLookups);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private const string Previous = "2.57.0.98304";
    private const string Current = "2.57.0.98348";

    [Fact]
    public async Task DownloadNextAsync_TakesTheCurrentPatchOverALowerIdOnAnOlderBuild()
    {
        string root = NewRoot();
        var service = new ListedDownloads(
            newest: 65580010,
            listPage: minId =>
                minId < 65580002
                    ? Builds((65580001, Previous), (65580002, Current))
                    : ReplayListing.Empty
        );

        try
        {
            HeroesProfileProvider provider = Standard(root, service);

            Assert.True(await provider.DownloadNextAsync());

            Assert.Equal(65580002, Assert.Single(service.Downloaded));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadNextAsync_TakesTheMostInterestingCandidate_AndListsPastTheOthers()
    {
        // #510: three current-patch candidates; 65580002 has a deathless carry.
        string root = NewRoot();
        var service = new ListedDownloads(
            newest: 65580010,
            listPage: minId =>
                minId switch
                {
                    < 65580003 => Builds(
                        (65580001, Current),
                        (65580002, Current),
                        (65580003, Current)
                    ),
                    < 65580004 => Builds((65580004, Current)),
                    _ => ReplayListing.Empty,
                }
        );
        var details = new Details(
            Game(65580001, 11),
            Game(65580002, 20, new ReplayScoreLine(0, "Valla", 12, 0, 9, 21, 21, 3, 240)),
            Game(65580003, 20)
        );

        try
        {
            HeroesProfileProvider provider = Standard(root, service, details: details);

            Assert.True(await provider.DownloadNextAsync());
            Assert.True(await provider.DownloadNextAsync());

            // The second listing starts past every scored candidate, not just past the one taken.
            Assert.Equal(new[] { 65580002, 65580004 }, service.Downloaded);
            Assert.Equal(new[] { 65580001, 65580002, 65580003, 65580004 }, details.Asked);
            Assert.Equal(new[] { 65580000, 65580003 }, service.Listed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadNextAsync_WithNoScoreScreen_TakesTheLowestIdAsBefore()
    {
        string root = NewRoot();
        var service = new ListedDownloads(
            newest: 65580010,
            listPage: minId =>
                minId < 65580003
                    ? Builds((65580001, Current), (65580002, Current), (65580003, Current))
                    : ReplayListing.Empty
        );

        try
        {
            HeroesProfileProvider provider = Standard(root, service, details: new Details());

            Assert.True(await provider.DownloadNextAsync());
            Assert.True(await provider.DownloadNextAsync());

            Assert.Equal(new[] { 65580001, 65580002 }, service.Downloaded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadNextAsync_ReadsAheadForTheCurrentPatch()
    {
        string root = NewRoot();
        var service = new ListedDownloads(
            newest: 65581010,
            listPage: minId =>
                minId switch
                {
                    < 65580001 => Builds((65580001, Previous)),
                    < 65581001 => Builds((65581001, Current)),
                    _ => ReplayListing.Empty,
                }
        );

        try
        {
            HeroesProfileProvider provider = Standard(root, service);

            Assert.True(await provider.DownloadNextAsync());

            Assert.Equal(65581001, Assert.Single(service.Downloaded));
            Assert.Equal(new[] { 65580000, 65580001 }, service.Listed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadNextAsync_FallsBackToThePreviousBuildWhenTheCurrentPatchIsNotListed()
    {
        string root = NewRoot();
        var service = new ListedDownloads(
            newest: 65580010,
            listPage: minId =>
                minId < 65580002
                    ? Builds((65580001, Previous), (65580002, Previous))
                    : ReplayListing.Empty
        );

        try
        {
            HeroesProfileProvider provider = Standard(root, service);

            Assert.True(await provider.DownloadNextAsync());

            Assert.Equal(65580001, Assert.Single(service.Downloaded));
            Assert.Equal(new[] { 65580000, 65580002 }, service.Listed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// #346: a download Heroes Profile answers with an HTTP status (after the pipeline's retries)
    /// is a logged skip, not an outage, and the next call takes the replay after it.
    /// </summary>
    [Fact]
    public async Task DownloadNextAsync_SkipsAReplayWhoseDownloadHeroesProfileRefuses()
    {
        string root = NewRoot();
        var service = new ListedDownloads(
            newest: 65590000,
            listPage: minId => Builds((minId + 1, Current))
        )
        {
            Refuse = id =>
                id == 65580001
                    ? new ApiException("Too Many Requests") { ResponseStatusCode = 429 }
                    : null,
        };
        var logger = new ListLogger();

        try
        {
            HeroesProfileProvider provider = Standard(root, service, logger);

            Assert.False(await provider.DownloadNextAsync());

            Assert.Empty(Directory.GetFiles(Path.Combine(root, "Standard")));
            LogLine skip = Assert.Single(
                logger.Lines,
                line => line.Message.StartsWith("Skipped replay", StringComparison.Ordinal)
            );
            Assert.Equal(65580001, skip.Values["ReplayId"]);
            Assert.Equal(429, skip.Values["Status"]);

            Assert.True(await provider.DownloadNextAsync());

            Assert.Equal(new[] { 65580001, 65580002 }, service.Downloaded);
            string file = Assert.Single(Directory.GetFiles(Path.Combine(root, "Standard")));
            Assert.StartsWith("65580002_", Path.GetFileName(file), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>No answer from Heroes Profile is still a failure for the role's outage count.</summary>
    [Fact]
    public async Task DownloadNextAsync_ANetworkFailureStillThrows()
    {
        string root = NewRoot();
        var service = new ListedDownloads(
            newest: 65590000,
            listPage: minId => Builds((minId + 1, Current))
        )
        {
            Refuse = _ => new HttpRequestException("No such host is known."),
        };

        try
        {
            HeroesProfileProvider provider = Standard(root, service);

            await Assert.ThrowsAsync<HttpRequestException>(() => provider.DownloadNextAsync());
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "Standard")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private const int Limit = 5;

    /// <summary>
    /// #280: 97 week-old replays of a re-downloaded build waited in Data\Standard and stopped
    /// every download. Only replays inside the media window count toward the limit.
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(Limit - 1, true)]
    [InlineData(Limit, false)]
    public async Task DownloadNextAsync_OnlyReplaysInsideTheMediaWindowCountTowardTheLimit(
        int fresh,
        bool downloads
    )
    {
        string root = NewRoot();
        Dictionary<int, DateTime> played = Backlog(expired: 97, fresh);
        var service = new ListedDownloads(
            newest: 65590000,
            listPage: minId => Builds((minId + 1, Current))
        );

        try
        {
            WriteStandard(root, played.Keys);
            HeroesProfileProvider provider = Standard(root, service);
            provider.UseReplayHeaders(path => Header(path, played));

            Assert.Equal(downloads, await provider.DownloadNextAsync());

            int downloaded = downloads ? 1 : 0;
            Assert.Equal(downloaded, service.Downloaded.Count);
            // The expired backlog stays on disk as filler.
            Assert.Equal(
                97 + fresh + downloaded,
                Directory.GetFiles(Path.Combine(root, "Standard")).Length
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// #433: while focus picks are on, Focus:ExtraCandidates more fresh replays wait, so a focus
    /// pick has a choice.
    /// </summary>
    [Theory]
    [InlineData(false, Limit, false)]
    [InlineData(true, Limit, true)]
    [InlineData(true, Limit + 5, false)]
    public async Task DownloadNextAsync_FocusKeepsExtraCandidatesWaiting(
        bool focus,
        int fresh,
        bool downloads
    )
    {
        string root = NewRoot();
        Dictionary<int, DateTime> played = Backlog(expired: 0, fresh);
        var service = new ListedDownloads(
            newest: 65590000,
            listPage: minId => Builds((minId + 1, Current))
        );

        try
        {
            WriteStandard(root, played.Keys);
            HeroesProfileProvider provider = Standard(root, service, focus: focus);
            provider.UseReplayHeaders(path => Header(path, played));

            Assert.Equal(downloads, await provider.DownloadNextAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadNextAsync_LogsTheWaitingCountAndTheFreshCountAgainstTheLimit()
    {
        string root = NewRoot();
        Dictionary<int, DateTime> played = Backlog(expired: 97, fresh: Limit);
        var service = new ListedDownloads(
            newest: 65590000,
            listPage: minId => Builds((minId + 1, Current))
        );
        var logger = new ListLogger();

        try
        {
            WriteStandard(root, played.Keys);
            HeroesProfileProvider provider = Standard(root, service, logger);
            provider.UseReplayHeaders(path => Header(path, played));

            Assert.False(await provider.DownloadNextAsync());

            Assert.Empty(service.Listed);
            LogLine line = Assert.Single(logger.Lines);
            Assert.Equal(
                "102 replays waiting to be spectated (5 fresh, limit 5). Not downloading another.",
                line.Message
            );
            Assert.Equal(102, line.Values["Waiting"]);
            Assert.Equal(Limit, line.Values["Fresh"]);
            Assert.Equal(Limit, line.Values["Limit"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A listed replay already past its window would never count toward the limit, so it keeps
    /// the old cap on every waiting replay. Otherwise expired downloads would never stop.
    /// </summary>
    [Theory]
    [InlineData(Limit - 1, true)]
    [InlineData(Limit, false)]
    public async Task DownloadNextAsync_AnExpiredListingKeepsTheCapOnEveryWaitingReplay(
        int expired,
        bool downloads
    )
    {
        string root = NewRoot();
        Dictionary<int, DateTime> played = Backlog(expired, fresh: 0);
        DateTime now = DateTime.UtcNow;
        var service = new ListedDownloads(
            newest: 65660000,
            listPage: minId => Replay(minId + 1, now - TimeSpan.FromDays(7))
        );

        try
        {
            WriteStandard(root, played.Keys);
            HeroesProfileProvider provider = Provider(
                root,
                service,
                resume: null,
                CancellationToken.None,
                enableRequests: false,
                maxReplayAge: TimeSpan.Zero
            );
            provider.UseReplayHeaders(path => Header(path, played));

            Assert.Equal(downloads, await provider.DownloadNextAsync());

            Assert.Equal(downloads ? 1 : 0, service.Downloaded.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A replay whose game date cannot be read counts, as every waiting replay did.</summary>
    [Fact]
    public async Task DownloadNextAsync_CountsAWaitingReplayWhoseGameDateCannotBeRead()
    {
        string root = NewRoot();
        var service = new ListedDownloads(
            newest: 65590000,
            listPage: minId => Builds((minId + 1, Current))
        );
        int reads = 0;

        try
        {
            WriteStandard(root, Backlog(expired: 0, fresh: Limit).Keys);
            HeroesProfileProvider provider = Standard(root, service);
            provider.UseReplayHeaders(_ =>
            {
                reads++;
                return null;
            });

            Assert.False(await provider.DownloadNextAsync());
            Assert.False(await provider.DownloadNextAsync());

            Assert.Empty(service.Downloaded);
            Assert.Equal(Limit, reads);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Week-old replays from 6553xxxx, then fresh ones from 6558xxxx.</summary>
    /// <summary>
    /// A focus target's game (#433) is fetched once, when it is a Storm League game spectate can
    /// launch and inside the focus window, which is longer than an ordinary replay's.
    /// </summary>
    [Fact]
    public async Task DownloadTargetedAsync_FetchesALaunchableStormLeagueGameInsideTheFocusWindow()
    {
        string root = NewRoot();
        try
        {
            DateTime now = DateTime.UtcNow;
            var byId = new Dictionary<int, HeroesProfileReplay>
            {
                [65590001] = Target(65590001, Current, now.AddDays(-5)),
                [65590002] = Target(65590002, "2.58.0.99999", now.AddHours(-2)),
                [65590003] = Target(65590003, Current, now.AddDays(-9)),
                [65590004] = Target(65590004, Current, now.AddHours(-2), "Quick Match"),
            };
            var service = new ListedDownloads(0, _ => ReplayListing.Empty)
            {
                ById = id => byId.GetValueOrDefault(id),
            };
            HeroesProfileProvider provider = Standard(root, service, focus: true);

            Assert.Equal(
                TargetedDownload.Downloaded,
                await provider.DownloadTargetedAsync(65590001, CancellationToken.None)
            );
            Assert.Equal(
                TargetedDownload.Known,
                await provider.DownloadTargetedAsync(65590001, CancellationToken.None)
            );
            Assert.Equal(
                TargetedDownload.NotLaunchable,
                await provider.DownloadTargetedAsync(65590002, CancellationToken.None)
            );
            Assert.Equal(
                TargetedDownload.Old,
                await provider.DownloadTargetedAsync(65590003, CancellationToken.None)
            );
            Assert.Equal(
                TargetedDownload.NotStormLeague,
                await provider.DownloadTargetedAsync(65590004, CancellationToken.None)
            );
            Assert.Equal(
                TargetedDownload.NotFound,
                await provider.DownloadTargetedAsync(65590005, CancellationToken.None)
            );
            Assert.Equal(new[] { 65590001 }, service.Downloaded);
            Assert.Single(
                Directory.GetFiles(Path.Combine(root, "Standard"), "65590001_Storm League_*")
            );
        }
        finally
        {
            TestTemp.Delete(root);
        }
    }

    private static HeroesProfileReplay Target(
        int id,
        string version,
        DateTime played,
        string gameType = "Storm League"
    ) =>
        new()
        {
            Id = id,
            GameType = gameType,
            GameVersion = version,
            Map = "Cursed Hollow",
            Fingerprint = "abc",
            GameDate = played.ToString("yyyy-MM-dd HH:mm:ss"),
        };

    private static Dictionary<int, DateTime> Backlog(int expired, int fresh)
    {
        DateTime now = DateTime.UtcNow;
        var played = new Dictionary<int, DateTime>();
        for (int index = 1; index <= expired; index++)
        {
            played[65530000 + index] = now - TimeSpan.FromDays(7);
        }

        for (int index = 1; index <= fresh; index++)
        {
            played[65580000 + index] = now - TimeSpan.FromHours(1);
        }

        return played;
    }

    private static void WriteStandard(string root, IEnumerable<int> ids)
    {
        string standard = Path.Combine(root, "Standard");
        Directory.CreateDirectory(standard);
        foreach (int id in ids)
        {
            File.WriteAllBytes(
                Path.Combine(
                    standard,
                    id + "_Storm League_Diamond 3_Cursed Hollow_abc.StormReplay"
                ),
                new byte[] { 1 }
            );
        }
    }

    private static Heroes.ReplayParser.Replay Header(
        string path,
        IReadOnlyDictionary<int, DateTime> played
    )
    {
        int id = int.Parse(Path.GetFileName(path).Split('_')[0]);
        return new Heroes.ReplayParser.Replay
        {
            Timestamp = played[id],
            ReplayVersion = "2.57.0.98285",
        };
    }

    private static HeroesProfileProvider Standard(
        string root,
        IHeroesProfileService service,
        ILogger<HeroesProfileProvider> logger = null,
        bool focus = false,
        IReplayDetailLookup details = null
    )
    {
        HeroesProfileProvider provider = Provider(
            root,
            service,
            resume: null,
            CancellationToken.None,
            enableRequests: false,
            logger: logger,
            focus: focus,
            details: details
        );
        provider.UseInstalledVersions(() => new[] { Previous, Current });
        return provider;
    }

    private static ReplayListing Builds(params (int Id, string Version)[] rows)
    {
        string played = (DateTime.UtcNow - TimeSpan.FromHours(1)).ToString("yyyy-MM-dd HH:mm:ss");
        var replays = new List<HeroesProfileReplay>();
        foreach ((int id, string version) in rows)
        {
            replays.Add(
                new HeroesProfileReplay
                {
                    Id = id,
                    GameType = "Storm League",
                    GameVersion = version,
                    Map = "Cursed Hollow",
                    Fingerprint = "abc",
                    GameDate = played,
                }
            );
        }

        int highest = rows[rows.Length - 1].Id;
        return new ReplayListing(replays, hadRows: true, highestId: highest, nextAfter: highest);
    }

    private static ReplayListing Replay(int id, DateTime played) =>
        new(
            new[]
            {
                new HeroesProfileReplay
                {
                    Id = id,
                    GameType = "Storm League",
                    Map = "Cursed Hollow",
                    Fingerprint = "abc",
                    GameDate = played.ToString("yyyy-MM-dd HH:mm:ss"),
                },
            },
            hadRows: true,
            highestId: id,
            nextAfter: id
        );

    private static string NewRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "hr-download-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        return root;
    }

    private static HeroesProfileProvider Provider(
        string root,
        IHeroesProfileService service,
        IHeroesProfileResume resume,
        CancellationToken token,
        bool enableRequests = true,
        TimeSpan? maxReplayAge = null,
        ILogger<HeroesProfileProvider> logger = null,
        bool focus = false,
        IReplayDetailLookup details = null
    )
    {
        var settings = new AppSettings
        {
            Focus = new HeroFocusSettings { Enabled = focus, ExtraCandidates = 5 },
            Location = new LocationSettings { DataDirectory = root },
            HeroesProfileApi = new HeroesProfileApiSettings
            {
                StandardCacheDirectoryName = "Standard",
                RequestsCacheDirectoryName = "Requests",
                MinReplayId = 65580000,
                GameTypes = new[] { "Storm League" },
                StandardMaxReplayAge = maxReplayAge ?? TimeSpan.FromHours(12),
                CachedReplayLimit = Limit,
                Interest = new ReplayInterestSettings { Enabled = details != null, Candidates = 3 },
            },
            ReplayMedia = new ReplayMediaPolicySettings
            {
                RecordingMode = ReplayRecordingMode.Selected,
                PublicationMode = ReplayPublicationMode.AllEligible,
                OrdinaryCandidateMaxAge = TimeSpan.FromDays(3),
            },
            StormReplay = new StormReplaySettings
            {
                Seperator = "_",
                WildCard = "*.StormReplay",
                FileExtension = ".StormReplay",
            },
            Twitch = new TwitchSettings { EnableRequests = enableRequests },
            Retention = new RetentionSettings { Enabled = false },
        };

        return new HeroesProfileProvider(
            logger ?? NullLogger<HeroesProfileProvider>.Instance,
            new NoLoader(),
            new ReplayHelper(NullLogger<ReplayHelper>.Instance, settings),
            new OneRequest(),
            service,
            new CancellationTokenProvider(token),
            settings,
            resume,
            details
        );
    }

    /// <summary>Each candidate's score screen by id; an id it does not know has none.</summary>
    private sealed class Details : IReplayDetailLookup
    {
        private readonly Dictionary<int, ReplayDetail> details;

        public Details(params ReplayDetail[] known)
        {
            details = new Dictionary<int, ReplayDetail>();
            foreach (ReplayDetail detail in known)
            {
                details[detail.ReplayId] = detail;
            }
        }

        public List<int> Asked { get; } = new();

        public Task<ReplayDetail> GetDetailAsync(int replayId, CancellationToken cancellationToken)
        {
            Asked.Add(replayId);
            return Task.FromResult(details.GetValueOrDefault(replayId));
        }
    }

    /// <summary>Ten players with one plain line each, and <paramref name="star"/> as player 0.</summary>
    private static ReplayDetail Game(int replayId, int minutes, ReplayScoreLine star = null)
    {
        var players = new List<ReplayScoreLine>
        {
            star ?? new ReplayScoreLine(0, "Hero0", 4, 4, 8, 12, 6, 2, 0),
        };
        for (int index = 1; index < 10; index++)
        {
            players.Add(
                new ReplayScoreLine(index < 5 ? 0 : 1, "Hero" + index, 4, 4, 8, 12, 6, 2, 0)
            );
        }

        return new ReplayDetail(replayId, minutes * 60, players);
    }

    private sealed class OneRequest : IRequestQueue
    {
        public Task<RewardQueueItem> PeekDownloadAsync(DateTimeOffset now) =>
            Task.FromResult(
                new RewardQueueItem
                {
                    HeroesProfileReplay = new HeroesProfileReplay
                    {
                        Id = 65582300,
                        GameType = "Storm League",
                        Rank = "Platinum 4",
                        Map = "Cursed Hollow",
                        Fingerprint = "abc",
                    },
                }
            );

        public Task<RequestCompletion> CompleteDownloadAsync(RewardQueueItem item, Action publish)
        {
            publish?.Invoke();
            return Task.FromResult(RequestCompletion.Completed);
        }

        public Task<RequestDownload> RetryDownloadLaterAsync(
            RewardQueueItem item,
            string error,
            DateTimeOffset now
        ) => Task.FromResult(new RequestDownload { Attempts = 1, LastError = error });

        public Task<bool> FailDownloadAsync(
            RewardQueueItem item,
            string reason,
            bool refundRequested,
            DateTimeOffset now
        ) => Task.FromResult(true);

        public Task<RewardResponse> EnqueueItemAsync(RewardRequest request) =>
            throw new NotSupportedException();

        public Task<int> GetItemsInQueue() => Task.FromResult(1);

        public Task<RewardQueueItem> FindByIndexAsync(int index) =>
            Task.FromResult<RewardQueueItem>(null);

        public Task<(RewardQueueItem Item, int Position)?> RemoveItemAsync(string login) =>
            Task.FromResult<(RewardQueueItem Item, int Position)?>(null);

        public Task<(RewardQueueItem Item, int Position)?> FindNextByLoginAsync(string login) =>
            Task.FromResult<(RewardQueueItem Item, int Position)?>(null);
    }

    private sealed record LogLine(string Message, IReadOnlyDictionary<string, object> Values);

    private sealed class ListLogger : ILogger<HeroesProfileProvider>
    {
        public List<LogLine> Lines { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            var values = new Dictionary<string, object>();
            if (state is IEnumerable<KeyValuePair<string, object>> pairs)
            {
                foreach (KeyValuePair<string, object> pair in pairs)
                {
                    values[pair.Key] = pair.Value;
                }
            }

            Lines.Add(new LogLine(formatter(state, exception), values));
        }
    }

    private sealed class NoLoader : IReplayLoader
    {
        public Task<Heroes.ReplayParser.Replay> LoadAsync(string path) =>
            throw new NotSupportedException();
    }

    private sealed class ListedDownloads : IHeroesProfileService
    {
        private readonly int newest;
        private readonly Func<int, ReplayListing> listPage;

        public ListedDownloads(int newest, Func<int, ReplayListing> listPage)
        {
            this.newest = newest;
            this.listPage = listPage;
        }

        /// <summary>Every download asked for, including one <see cref="Refuse"/> fails.</summary>
        public List<int> Downloaded { get; } = new();

        /// <summary>The error a replay's download throws, or null to write it.</summary>
        public Func<int, Exception> Refuse { get; init; } = _ => null;

        public async Task DownloadReplayAsync(
            int replayId,
            Stream destination,
            CancellationToken cancellationToken
        )
        {
            Downloaded.Add(replayId);
            if (Refuse(replayId) is Exception refused)
            {
                throw refused;
            }

            await destination.WriteAsync(new byte[] { 1 }, cancellationToken);
        }

        public int MaxIdLookups { get; private set; }

        public Task<int> GetMaxReplayIdAsync()
        {
            MaxIdLookups++;
            return Task.FromResult(newest);
        }

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByFilters(
            GameType? gameType = null,
            GameRank? gameRank = null,
            string gameMap = null
        ) => throw new NotSupportedException();

        /// <summary>The replay `/replays` lists for an id, or null when it lists none.</summary>
        public Func<int, HeroesProfileReplay> ById { get; init; } =
            _ => throw new NotSupportedException();

        public Task<HeroesProfileReplay> GetReplayByIdAsync(int replayId) =>
            Task.FromResult(ById(replayId));

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByMinId(int minId) =>
            throw new NotSupportedException();

        public List<int> Listed { get; } = new();

        public Task<ReplayListing> ListPageAsync(int minId)
        {
            Listed.Add(minId);
            return Task.FromResult(listPage(minId));
        }

        public Task<IReadOnlyList<HeroesProfileReplay>> ListAfterAsync(
            int after,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task EnrichRankAsync(
            HeroesProfileReplay replay,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }

    private sealed class ScriptedDownloads : IHeroesProfileService
    {
        private readonly Func<Stream, CancellationToken, Task> write;

        public ScriptedDownloads(Func<Stream, CancellationToken, Task> write)
        {
            this.write = write;
        }

        public int Downloads { get; private set; }

        public bool FailFirst { get; set; }

        public async Task DownloadReplayAsync(
            int replayId,
            Stream destination,
            CancellationToken cancellationToken
        )
        {
            Downloads++;
            await write(destination, cancellationToken);
            if (FailFirst && Downloads == 1)
            {
                throw new IOException("connection reset");
            }
        }

        public Task<int> GetMaxReplayIdAsync() => Task.FromResult(0);

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByFilters(
            GameType? gameType = null,
            GameRank? gameRank = null,
            string gameMap = null
        ) => Task.FromResult<IEnumerable<HeroesProfileReplay>>(Array.Empty<HeroesProfileReplay>());

        public Task<HeroesProfileReplay> GetReplayByIdAsync(int replayId) =>
            Task.FromResult<HeroesProfileReplay>(null);

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByMinId(int minId) =>
            Task.FromResult<IEnumerable<HeroesProfileReplay>>(Array.Empty<HeroesProfileReplay>());

        public Task<ReplayListing> ListPageAsync(int minId) => Task.FromResult(ReplayListing.Empty);

        public Task<IReadOnlyList<HeroesProfileReplay>> ListAfterAsync(
            int after,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<IReadOnlyList<HeroesProfileReplay>>(Array.Empty<HeroesProfileReplay>());

        public Task EnrichRankAsync(
            HeroesProfileReplay replay,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;
    }
}
