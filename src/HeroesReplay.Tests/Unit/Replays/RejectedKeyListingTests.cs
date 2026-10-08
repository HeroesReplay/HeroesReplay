using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Twitch;
using HeroesReplay.HeroesProfile.Client;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Replays;

/// <summary>
/// #358: the download role listed Heroes Profile about every 1.5 s while it refused the API key
/// (about 1,200 calls an hour). The whole chain runs over a fake HTTP handler (no network): the
/// Heroes Profile client with its resilience pipeline, <see cref="HeroesProfileService"/>, and the
/// provider's download pass.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class RejectedKeyListingTests : IDisposable
{
    private const int ReplayId = 65580001;
    private const string Current = "2.57.0.98348";
    private static readonly DateTimeOffset Start = new(2026, 10, 8, 16, 1, 5, TimeSpan.Zero);
    private static readonly TimeSpan ProbeWait = new ServiceHealthSettings().NextDependencyProbe(
        failed: true
    );

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-rejected-key-" + Guid.NewGuid().ToString("N")
    );
    private readonly ScriptedHttp http = new();
    private readonly ServiceProvider services;
    private readonly ListLogger log = new();
    private DateTimeOffset now = Start;

    public RejectedKeyListingTests()
    {
        Directory.CreateDirectory(root);
        services = HeroesProfileHttpServices(http);
    }

    public void Dispose()
    {
        services.Dispose();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ARefusedKey_ListsNoMoreOftenThanTheProbe_AndWarnsOnce(HttpStatusCode refused)
    {
        http.List = () => new HttpResponseMessage(refused);
        HeroesProfileProvider provider = Provider();

        // The role's loop runs a pass every 15 s after an empty one.
        for (int pass = 0; pass < 8; pass++)
        {
            Assert.False(await provider.DownloadNextAsync());
            now += TimeSpan.FromSeconds(15);
        }

        Assert.Equal(1, http.Lists);

        now = Start + ProbeWait;
        for (int pass = 0; pass < 8; pass++)
        {
            Assert.False(await provider.DownloadNextAsync());
            now += TimeSpan.FromSeconds(15);
        }

        Assert.Equal(2, http.Lists);
        Assert.Empty(http.Downloads);
        string warning = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Warning, log.Levels.Single());
        Assert.Contains($"(HTTP {(int)refused})", warning);
    }

    [Fact]
    public async Task ARefusedKey_ListsAgainAtOnceWhenTheProbePasses()
    {
        http.List = () => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        HeroesProfileProvider provider = Provider();
        Assert.False(await provider.DownloadNextAsync());
        Assert.Equal(1, http.Lists);

        http.List = Listed;
        now += TimeSpan.FromSeconds(15);
        provider.ObserveDependency(Ok());

        Assert.True(await provider.DownloadNextAsync());
        Assert.Equal(2, http.Lists);
        Assert.Equal(new[] { ReplayId }, http.Downloads);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "Standard")));
        Assert.Equal(
            "The Heroes Profile probe passed. Replay listing resumes.",
            Assert.Single(
                log.Lines,
                line => line.Contains("listing resumes", StringComparison.Ordinal)
            )
        );
    }

    [Fact]
    public async Task ARefusedKey_ResumesWhenHeroesProfileAnswersAfterTheWait()
    {
        http.List = () => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        HeroesProfileProvider provider = Provider();
        Assert.False(await provider.DownloadNextAsync());

        http.List = Listed;
        now = Start + ProbeWait - TimeSpan.FromSeconds(1);
        Assert.False(await provider.DownloadNextAsync());
        Assert.Equal(1, http.Lists);

        now = Start + ProbeWait;
        Assert.True(await provider.DownloadNextAsync());
        Assert.Equal(2, http.Lists);

        // Listing is back to the normal pace: the next pass lists at once.
        now += TimeSpan.FromSeconds(15);
        http.List = () => Page(ReplayId + 1);
        Assert.True(await provider.DownloadNextAsync());
        Assert.Equal(3, http.Lists);
        Assert.Equal(new[] { ReplayId, ReplayId + 1 }, http.Downloads);
        Assert.Equal(
            "Heroes Profile answered the replay list. Replay listing resumes.",
            Assert.Single(
                log.Lines,
                line => line.Contains("listing resumes", StringComparison.Ordinal)
            )
        );
    }

    /// <summary>The probe ran before ready and found the key rejected: no list call at all.</summary>
    [Fact]
    public async Task ARejectedProbe_SkipsTheListUntilItPasses()
    {
        http.List = Listed;
        HeroesProfileProvider provider = Provider();
        provider.ObserveDependency(
            ServiceDependencyResult.Rejected(
                HeroesProfileApiProbe.DependencyName,
                HeroesProfileApiProbe.RejectedCode,
                "Heroes Profile rejected HeroesProfileApi:ApiKey (HTTP 401).",
                "fix the key"
            )
        );

        for (int pass = 0; pass < 20; pass++)
        {
            Assert.False(await provider.DownloadNextAsync());
            now += TimeSpan.FromSeconds(15);
        }

        Assert.Equal(0, http.Lists);

        provider.ObserveDependency(Ok());

        Assert.True(await provider.DownloadNextAsync());
        Assert.Equal(1, http.Lists);
    }

    /// <summary>
    /// A 429 stays transient, as before: the pipeline waits its Retry-After and retries, the pass
    /// lists again, the next pass lists at once, and nothing pauses.
    /// </summary>
    [Fact]
    public async Task ARateLimitedList_IsUnchanged()
    {
        http.List = RateLimited;
        HeroesProfileProvider provider = Provider();

        Assert.False(await provider.DownloadNextAsync());
        int firstPass = http.Lists;
        Assert.False(await provider.DownloadNextAsync());

        // More than the pipeline's own attempts: the provider listed again within the pass.
        Assert.True(firstPass > HeroesProfileHttp.MaxRetryAttempts + 1);
        Assert.Equal(firstPass * 2, http.Lists);
        // Nothing paused, so nothing to warn about or resume.
        Assert.Empty(log.Lines);

        http.List = Listed;
        Assert.True(await provider.DownloadNextAsync());
        Assert.Equal(new[] { ReplayId }, http.Downloads);
    }

    private HeroesProfileProvider Provider()
    {
        var settings = new AppSettings
        {
            Location = new LocationSettings { DataDirectory = root },
            HeroesProfileApi = new HeroesProfileApiSettings
            {
                StandardCacheDirectoryName = "Standard",
                RequestsCacheDirectoryName = "Requests",
                MinReplayId = ReplayId - 1,
                GameTypes = new[] { "Storm League" },
                StandardMaxReplayAge = TimeSpan.Zero,
                CachedReplayLimit = 5,
                APIRetryWaitTime = TimeSpan.Zero,
            },
            StormReplay = new StormReplaySettings
            {
                Seperator = "_",
                WildCard = "*.StormReplay",
                FileExtension = ".StormReplay",
            },
            Twitch = new TwitchSettings { EnableRequests = false },
            Retention = new RetentionSettings { Enabled = false },
        };
        var heroesProfile = new HeroesProfileService(
            NullLogger<HeroesProfileService>.Instance,
            new MemoryCache(new MemoryCacheOptions()),
            new CancellationTokenProvider(CancellationToken.None),
            settings,
            HeroesProfileClientFactory.Create(
                "test-key",
                services
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(HeroesProfileHttp.ClientName),
                new Uri("https://www.heroesprofile.com/api/external/v1/")
            )
        );
        var provider = new HeroesProfileProvider(
            log,
            new NoLoader(),
            new ReplayHelper(NullLogger<ReplayHelper>.Instance, settings),
            new NoRequests(),
            heroesProfile,
            new CancellationTokenProvider(CancellationToken.None),
            settings
        );
        provider.UseClock(() => now);
        provider.UseInstalledVersions(() => new[] { Current });
        provider.UseReplayHeaders(_ => new Heroes.ReplayParser.Replay
        {
            Timestamp = DateTime.UtcNow.AddHours(-1),
            ReplayVersion = Current,
        });
        return provider;
    }

    private static ServiceDependencyResult Ok() =>
        ServiceDependencyResult.Ok(HeroesProfileApiProbe.DependencyName, "accepted");

    private static HttpResponseMessage Listed() => Page(ReplayId);

    private static HttpResponseMessage Page(params int[] ids)
    {
        string played = DateTime.UtcNow.AddHours(-1).ToString("yyyy-MM-dd HH:mm:ss");
        string rows = string.Join(
            ",",
            ids.Select(id =>
                $$"""{"replayID":{{id}},"game_type":"Storm League","game_version":"{{Current}}","game_map":"Cursed Hollow","game_date":"{{played}}","fingerprint":"abc","downloadable":true,"deleted":0,"region":1,"parsed":1}"""
            )
        );
        int max = ids.Max();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"max_replay_id":{{max}},"next_after":{{max}},"replays":[{{rows}}]}""",
                Encoding.UTF8,
                "application/json"
            ),
        };
    }

    private static HttpResponseMessage RateLimited()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        return response;
    }

    private static ServiceProvider HeroesProfileHttpServices(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddHttpClient(
                HeroesProfileHttp.ClientName,
                client => client.Timeout = Timeout.InfiniteTimeSpan
            )
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler(
                HeroesProfileHttp.ClientName,
                builder => HeroesProfileHttp.Configure(builder, TimeSpan.Zero)
            );
        return services.BuildServiceProvider();
    }

    /// <summary>Answers the replay list with <see cref="List"/> and every download with one byte.</summary>
    private sealed class ScriptedHttp : HttpMessageHandler
    {
        public Func<HttpResponseMessage> List { get; set; } =
            () => new HttpResponseMessage(HttpStatusCode.NotFound);

        public int Lists { get; private set; }

        public List<int> Downloads { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/replays", StringComparison.Ordinal))
            {
                Lists++;
                return Task.FromResult(List());
            }

            if (path.EndsWith("/download/replay", StringComparison.Ordinal))
            {
                string query = request.RequestUri.Query;
                Downloads.Add(int.Parse(query[(query.IndexOf('=') + 1)..]));
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(new byte[] { 1 }),
                    }
                );
            }

            // The rank lookup: no player MMR, so the badge hides.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class NoLoader : IReplayLoader
    {
        public Task<Heroes.ReplayParser.Replay> LoadAsync(string path) =>
            throw new NotSupportedException();
    }

    private sealed class NoRequests : IRequestQueue
    {
        public Task<RewardQueueItem> PeekDownloadAsync(DateTimeOffset now) =>
            Task.FromResult<RewardQueueItem>(null);

        public Task<RequestCompletion> CompleteDownloadAsync(
            RewardQueueItem item,
            Action publish
        ) => throw new NotSupportedException();

        public Task<RequestDownload> RetryDownloadLaterAsync(
            RewardQueueItem item,
            string error,
            DateTimeOffset now
        ) => throw new NotSupportedException();

        public Task<bool> FailDownloadAsync(
            RewardQueueItem item,
            string reason,
            bool refundRequested,
            DateTimeOffset now
        ) => throw new NotSupportedException();

        public Task<RewardResponse> EnqueueItemAsync(RewardRequest request) =>
            throw new NotSupportedException();

        public Task<int> GetItemsInQueue() => Task.FromResult(0);

        public Task<RewardQueueItem> FindByIndexAsync(int index) =>
            Task.FromResult<RewardQueueItem>(null);

        public Task<(RewardQueueItem Item, int Position)?> RemoveItemAsync(string login) =>
            Task.FromResult<(RewardQueueItem Item, int Position)?>(null);

        public Task<(RewardQueueItem Item, int Position)?> FindNextByLoginAsync(string login) =>
            Task.FromResult<(RewardQueueItem Item, int Position)?>(null);
    }

    /// <summary>Warnings and up, plus the resume line, from the provider and its backoff.</summary>
    private sealed class ListLogger : ILogger<HeroesProfileProvider>
    {
        public List<string> Lines { get; } = new();

        public List<LogLevel> Levels { get; } = new();

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
            string message = formatter(state, exception);
            if (
                logLevel >= LogLevel.Warning
                || message.Contains("listing resumes", StringComparison.Ordinal)
            )
            {
                Lines.Add(message);
                Levels.Add(logLevel);
            }
        }
    }
}
