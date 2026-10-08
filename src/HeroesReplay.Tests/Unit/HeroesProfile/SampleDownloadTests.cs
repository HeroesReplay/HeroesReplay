using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.Commands.HeroesProfile.Commands;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Shared;
using HeroesReplay.HeroesProfile.Client;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Kiota.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

/// <summary>#346: <c>heroesprofile sample</c> skips a download Heroes Profile rejects and keeps going.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class SampleDownloadTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "hr-sample-" + Guid.NewGuid().ToString("N")
    );

    public SampleDownloadTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public async Task AThrownDownloadAndA429_AreSkipped_AndTheNextListedReplaysFillTheCount()
    {
        var downloader = new FakeDownloader
        {
            [104] = Bytes(4),
            // Writes part of the file, then the connection drops.
            [103] = async (destination, token) =>
            {
                await destination.WriteAsync(new byte[] { 1, 2 }, token);
                throw new IOException("connection reset");
            },
            // What Kiota throws once the HTTP pipeline has given up on a 429.
            [102] = (_, _) =>
                throw new ApiException("Too Many Requests") { ResponseStatusCode = 429 },
            [101] = Bytes(1),
            [100] = Bytes(0),
        };
        var logger = new ListLogger();
        var output = new StringWriter();

        SampleOutcome outcome = await new SampleDownload(downloader, logger, output).RunAsync(
            Listed(104, 103, 102, 101, 100),
            count: 2,
            folder,
            Name,
            CancellationToken.None
        );

        Assert.Equal(new[] { 104, 103, 102, 101 }, downloader.Tried);
        Assert.Equal(new[] { Name(104), Name(101) }, outcome.Downloaded);
        Assert.Empty(outcome.AlreadyThere);
        Assert.Equal(
            new[]
            {
                new SampleSkip(103, null, "IOException: connection reset"),
                new SampleSkip(102, 429, "HTTP 429"),
            },
            outcome.Skipped
        );
        Assert.Equal(0, outcome.ExitCode);
        Assert.Equal(new byte[] { 4 }, File.ReadAllBytes(Path.Combine(folder, Name(104))));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(folder, Name(101))));
        // No partial file and no file for a skipped replay.
        Assert.Equal(
            new[] { Name(101), Name(104) },
            Directory.GetFiles(folder).Select(Path.GetFileName).Order()
        );
        Assert.Equal(
            new[] { (103, (int?)null), (102, (int?)429) },
            logger
                .Warnings.Select(line =>
                    ((int)line.Values["ReplayId"], line.Values.GetValueOrDefault("Status") as int?)
                )
                .ToArray()
        );
        Assert.Contains($"Downloaded {Name(101)} (2.57.0.98348).", output.ToString());
        Assert.Equal(
            new[]
            {
                $"2 downloaded, 0 already in {folder}, 2 skipped.",
                "Skipped 103: IOException: connection reset.",
                "Skipped 102: HTTP 429.",
            },
            outcome.Summary(folder)
        );
    }

    [Fact]
    public async Task TheListingRunsOut_KeepsWhatWasDownloaded()
    {
        var downloader = new FakeDownloader
        {
            [101] = (_, _) =>
                throw new ApiException("Too Many Requests") { ResponseStatusCode = 429 },
            [100] = Bytes(0),
        };

        SampleOutcome outcome = await new SampleDownload(
            downloader,
            new ListLogger(),
            TextWriter.Null
        ).RunAsync(Listed(101, 100), count: 3, folder, Name, CancellationToken.None);

        Assert.Equal(new[] { Name(100) }, outcome.Downloaded);
        Assert.Equal(101, Assert.Single(outcome.Skipped).ReplayId);
        Assert.Equal(0, outcome.ExitCode);
    }

    [Fact]
    public async Task EveryDownloadSkipped_ExitsOne()
    {
        var downloader = new FakeDownloader
        {
            [101] = (_, _) => throw new ApiException("Unauthorized") { ResponseStatusCode = 401 },
            [100] = (_, _) => throw new IOException("connection reset"),
        };

        SampleOutcome outcome = await new SampleDownload(
            downloader,
            new ListLogger(),
            TextWriter.Null
        ).RunAsync(Listed(101, 100), count: 2, folder, Name, CancellationToken.None);

        Assert.Empty(outcome.Downloaded);
        Assert.Equal(2, outcome.Skipped.Count);
        Assert.Equal(1, outcome.ExitCode);
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task AReplayAlreadyInTheFolder_CountsAndIsNotDownloadedAgain()
    {
        File.WriteAllBytes(Path.Combine(folder, Name(101)), new byte[] { 7 });
        var downloader = new FakeDownloader { [101] = Bytes(1), [100] = Bytes(0) };

        SampleOutcome outcome = await new SampleDownload(
            downloader,
            new ListLogger(),
            TextWriter.Null
        ).RunAsync(Listed(101, 100), count: 1, folder, Name, CancellationToken.None);

        Assert.Empty(downloader.Tried);
        Assert.Equal(new[] { Name(101) }, outcome.AlreadyThere);
        Assert.Equal(0, outcome.ExitCode);
    }

    [Fact]
    public async Task Cancelling_StopsTheRun_AndLeavesNoPartialFile()
    {
        using var stop = new CancellationTokenSource();
        var downloader = new FakeDownloader
        {
            [101] = async (destination, token) =>
            {
                await destination.WriteAsync(new byte[] { 1 }, token);
                stop.Cancel();
                token.ThrowIfCancellationRequested();
            },
            [100] = Bytes(0),
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SampleDownload(downloader, new ListLogger(), TextWriter.Null).RunAsync(
                Listed(101, 100),
                count: 2,
                folder,
                Name,
                stop.Token
            )
        );

        Assert.Equal(new[] { 101 }, downloader.Tried);
        Assert.Empty(Directory.GetFiles(folder));
    }

    /// <summary>
    /// The whole chain over a fake HTTP handler (no network): the Heroes Profile client and its
    /// resilience pipeline retry a 429 after its Retry-After, then Kiota throws the status and the
    /// sample skips that replay and takes the next one.
    /// </summary>
    [Fact]
    public async Task ThroughTheHeroesProfileClient_ARateLimitedReplayIsRetriedThenSkipped()
    {
        var handler = new ScriptedHttp(request =>
            request.RequestUri.Query.Contains("replayID=102", StringComparison.Ordinal)
                ? RateLimited()
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 5 }),
                }
        );
        using ServiceProvider services = HeroesProfileHttpServices(handler);
        var heroesProfile = new HeroesProfileService(
            NullLogger<HeroesProfileService>.Instance,
            new MemoryCache(new MemoryCacheOptions()),
            new CancellationTokenProvider(CancellationToken.None),
            new AppSettings(),
            HeroesProfileClientFactory.Create(
                "test-key",
                services
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(HeroesProfileHttp.ClientName),
                new Uri("https://www.heroesprofile.com/api/external/v1/")
            )
        );

        SampleOutcome outcome = await new SampleDownload(
            heroesProfile,
            new ListLogger(),
            TextWriter.Null
        ).RunAsync(Listed(102, 101), count: 1, folder, Name, CancellationToken.None);

        Assert.Equal(new SampleSkip(102, 429, "HTTP 429"), Assert.Single(outcome.Skipped));
        Assert.Equal(new[] { Name(101) }, outcome.Downloaded);
        Assert.Equal(new byte[] { 5 }, File.ReadAllBytes(Path.Combine(folder, Name(101))));
        // The first try and the pipeline's retries, then the next listed replay once.
        Assert.Equal(
            HeroesProfileHttp.MaxRetryAttempts + 1,
            handler.Queries.Count(query => query.Contains("replayID=102", StringComparison.Ordinal))
        );
        Assert.Equal(
            1,
            handler.Queries.Count(query => query.Contains("replayID=101", StringComparison.Ordinal))
        );
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
            .AddResilienceHandler(HeroesProfileHttp.ClientName, HeroesProfileHttp.Configure);
        return services.BuildServiceProvider();
    }

    private sealed class ScriptedHttp : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;

        public ScriptedHttp(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            this.respond = respond;
        }

        public List<string> Queries { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Queries.Add(request.RequestUri.Query);
            return Task.FromResult(respond(request));
        }
    }

    private static string Name(int id) =>
        $"{id}_Storm League_Unknown_Hanamura Temple_abc.StormReplay";

    private static string Name(HeroesProfileReplay replay) => Name(replay.Id);

    private static List<HeroesProfileReplay> Listed(params int[] ids) =>
        ids.Select(id => new HeroesProfileReplay
            {
                Id = id,
                GameType = "Storm League",
                GameVersion = "2.57.0.98348",
                Map = "Hanamura Temple",
                Fingerprint = "abc",
            })
            .ToList();

    private static Func<Stream, CancellationToken, Task> Bytes(byte value) =>
        (destination, token) => destination.WriteAsync(new[] { value }, token).AsTask();

    /// <summary>A downloader scripted per replay id. Nothing else is called.</summary>
    private sealed class FakeDownloader : IHeroesProfileService
    {
        private readonly Dictionary<int, Func<Stream, CancellationToken, Task>> downloads = new();

        public Func<Stream, CancellationToken, Task> this[int replayId]
        {
            set => downloads[replayId] = value;
        }

        public List<int> Tried { get; } = new();

        public Task DownloadReplayAsync(
            int replayId,
            Stream destination,
            CancellationToken cancellationToken
        )
        {
            Tried.Add(replayId);
            return downloads[replayId](destination, cancellationToken);
        }

        public Task<int> GetMaxReplayIdAsync() => throw new NotSupportedException();

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByFilters(
            GameType? gameType = null,
            GameRank? gameRank = null,
            string gameMap = null
        ) => throw new NotSupportedException();

        public Task<HeroesProfileReplay> GetReplayByIdAsync(int replayId) =>
            throw new NotSupportedException();

        public Task<IEnumerable<HeroesProfileReplay>> GetReplaysByMinId(int minId) =>
            throw new NotSupportedException();

        public Task<ReplayListing> ListPageAsync(int minId) => throw new NotSupportedException();

        public Task<IReadOnlyList<HeroesProfileReplay>> ListAfterAsync(
            int after,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task EnrichRankAsync(
            HeroesProfileReplay replay,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    private sealed record LogLine(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object> Values
    );

    private sealed class ListLogger : ILogger
    {
        public List<LogLine> Lines { get; } = new();

        public IEnumerable<LogLine> Warnings => Lines.Where(line => line.Level == LogLevel.Warning);

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

            Lines.Add(new LogLine(logLevel, formatter(state, exception), values));
        }
    }
}
