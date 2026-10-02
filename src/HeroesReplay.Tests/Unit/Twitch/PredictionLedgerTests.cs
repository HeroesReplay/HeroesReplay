using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Spectating.Capture;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.Twitch.Predictions;
using Microsoft.Extensions.Logging.Abstractions;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Core.Exceptions;
using TwitchLib.Api.Core.Interfaces;
using TwitchLib.Api.Helix;
using TwitchLib.Api.Interfaces;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PredictionLedgerTests
{
    private const string BroadcasterId = "487238352";
    private const string BlueOutcomeId = "blue-outcome";
    private const string RedOutcomeId = "red-outcome";

    [Fact]
    public async Task Open_AfterRestart_AdoptsStoredPredictionId()
    {
        string directory = NewDirectory();
        try
        {
            Seed(directory, 42, 1, "pred-a", "Cursed Hollow: who wins?");
            var http = new PredictionHttp();
            http.Items.Add(Item("pred-a", "Cursed Hollow: who wins?", "ACTIVE"));

            TwitchMatchPredictionService started = Service(directory, http);
            PredictionReconcileResult first = await started.ReconcileAsync(CancellationToken.None);
            TwitchMatchPredictionService restarted = Service(directory, http);
            PredictionReconcileResult second = await restarted.ReconcileAsync(
                CancellationToken.None
            );
            bool opened = await restarted.OpenAsync(42, "Cursed Hollow", CancellationToken.None);

            Assert.Equal("pred-a", first.Resume.PredictionId);
            Assert.Equal(42, first.Resume.ReplayId);
            Assert.Equal(1, first.Resume.Attempt);
            Assert.Equal("pred-a", second.Resume.PredictionId);
            Assert.Empty(second.ForeignPredictionIds);
            Assert.True(opened);
            Assert.Equal(0, http.CreateCount);
            Assert.Equal(0, http.PatchCount);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Open_ForeignLockedPrediction_IsCanceledAndANewOneOpens()
    {
        string directory = NewDirectory();
        try
        {
            Seed(directory, 10, 1, "pred-a", "Cursed Hollow: who wins?");
            var http = new PredictionHttp();
            http.Items.Add(Item("pred-b", "Cursed Hollow: who wins?", "LOCKED"));
            TwitchMatchPredictionService service = Service(directory, http);

            bool opened = await service.OpenAsync(11, "Cursed Hollow", CancellationToken.None);
            PredictionLedger reloaded = PredictionLedger.Load(PredictionLedger.PathFor(directory));

            Assert.True(opened);
            Assert.Equal(1, http.PatchCount);
            Assert.Contains("pred-b", http.LastPatch, StringComparison.Ordinal);
            Assert.Contains("CANCELED", http.LastPatch, StringComparison.Ordinal);
            Assert.Equal(1, http.CreateCount);
            Assert.Null(reloaded.FindByPredictionId("pred-b"));
            Assert.Equal(11, reloaded.FindByPredictionId("created-prediction").ReplayId);
            Assert.Equal(PredictionLedgerState.Open, reloaded.FindByPredictionId("pred-a").State);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Open_PreviousReplaysPrediction_IsCanceledSettledAndReplaced()
    {
        string directory = NewDirectory();
        try
        {
            Seed(directory, 10, 1, "pred-a", "Cursed Hollow: who wins?");
            var http = new PredictionHttp();
            http.Items.Add(Item("pred-a", "Cursed Hollow: who wins?", "LOCKED"));
            TwitchMatchPredictionService service = Service(directory, http);

            bool opened = await service.OpenAsync(11, "Cursed Hollow", CancellationToken.None);
            PredictionLedger reloaded = PredictionLedger.Load(PredictionLedger.PathFor(directory));
            PredictionLedgerEntry previous = reloaded.FindByPredictionId("pred-a");

            Assert.True(opened);
            Assert.Contains("CANCELED", http.LastPatch, StringComparison.Ordinal);
            Assert.Equal(PredictionLedgerState.Settled, previous.State);
            Assert.Equal("CANCELED", previous.SettledStatus);
            Assert.Equal(1, http.CreateCount);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Open_WhenLeftoverCannotBeCanceled_DoesNotCreate()
    {
        string directory = NewDirectory();
        try
        {
            var http = new PredictionHttp { FailPatch = true };
            http.Items.Add(Item("pred-b", "Cursed Hollow: who wins?", "LOCKED"));
            TwitchMatchPredictionService service = Service(directory, http);

            bool opened = await service.OpenAsync(11, "Cursed Hollow", CancellationToken.None);

            Assert.False(opened);
            Assert.Equal(1, http.PatchCount);
            Assert.Equal(0, http.CreateCount);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Open_LockedStoredPrediction_IsAdoptedAndNotCanceled()
    {
        string directory = NewDirectory();
        try
        {
            Seed(directory, 10, 1, "pred-a", "Cursed Hollow: who wins?");
            var http = new PredictionHttp();
            http.Items.Add(Item("pred-a", "Towers of Doom: who wins?", "LOCKED"));
            TwitchMatchPredictionService service = Service(directory, http);

            bool opened = await service.OpenAsync(10, "Cursed Hollow", CancellationToken.None);
            Assert.True(opened);
            Assert.Equal(0, http.PatchCount);
            Assert.Null(http.LastPatch);

            await service.ResolveReplayAsync(10, 1, CancellationToken.None);
            PredictionLedgerEntry saved = PredictionLedger
                .Load(PredictionLedger.PathFor(directory))
                .FindByPredictionId("pred-a");

            Assert.Contains("\"RESOLVED\"", http.LastPatch, StringComparison.Ordinal);
            Assert.Contains(RedOutcomeId, http.LastPatch, StringComparison.Ordinal);
            Assert.DoesNotContain("CANCELED", http.LastPatch, StringComparison.Ordinal);
            Assert.Equal(1, http.PatchCount);
            Assert.Equal(PredictionLedgerState.Settled, saved.State);
            Assert.Equal(PredictionIntent.None, saved.Intent);
            Assert.Equal("RESOLVED", saved.SettledStatus);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task ResolveReplay_WhenEndPredictionFails_KeepsPendingIntent()
    {
        string directory = NewDirectory();
        try
        {
            Seed(directory, 10, 1, "pred-a", "Cursed Hollow: who wins?");
            var http = new PredictionHttp { FailPatch = true };
            http.Items.Add(Item("pred-a", "Cursed Hollow: who wins?", "LOCKED"));
            TwitchMatchPredictionService service = Service(directory, http);

            await service.ResolveReplayAsync(10, 0, CancellationToken.None);
            await service.ResolveReplayAsync(10, 1, CancellationToken.None);
            PredictionLedgerEntry saved = PredictionLedger
                .Load(PredictionLedger.PathFor(directory))
                .FindByPredictionId("pred-a");

            Assert.Equal(1, http.PatchCount);
            Assert.Equal("pred-a", saved.PredictionId);
            Assert.Equal(PredictionLedgerState.Pending, saved.State);
            Assert.Equal(PredictionIntent.Resolve, saved.Intent);
            Assert.Equal(0, saved.WinningTeam);
            Assert.Equal("10:1", saved.SessionKey);
            Assert.True(saved.FailureCount >= 1);
            Assert.NotNull(saved.NextAttemptAt);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task ResolveReplay_ConfirmedCompletion_SecondCallDoesNotSettleAgain()
    {
        string directory = NewDirectory();
        try
        {
            Seed(directory, 10, 1, "pred-a", "Cursed Hollow: who wins?");
            var http = new PredictionHttp();
            http.Items.Add(Item("pred-a", "Cursed Hollow: who wins?", "ACTIVE"));
            TwitchMatchPredictionService service = Service(directory, http);

            await service.ResolveReplayAsync(10, 0, CancellationToken.None);
            await service.ResolveReplayAsync(10, 0, CancellationToken.None);
            PredictionLedgerEntry saved = PredictionLedger
                .Load(PredictionLedger.PathFor(directory))
                .FindByPredictionId("pred-a");

            Assert.Equal(1, http.PatchCount);
            Assert.Equal(PredictionLedgerState.Settled, saved.State);
            Assert.Equal(PredictionIntent.None, saved.Intent);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Reconcile_MissingRemotePrediction_ReportsOrphanWithoutGuessing()
    {
        string directory = NewDirectory();
        try
        {
            Seed(directory, 10, 1, "pred-a", "Cursed Hollow: who wins?");
            TwitchMatchPredictionService service = Service(directory, new PredictionHttp());

            PredictionReconcileResult result = await service.ReconcileAsync(CancellationToken.None);
            PredictionLedgerEntry saved = PredictionLedger
                .Load(PredictionLedger.PathFor(directory))
                .FindByPredictionId("pred-a");

            Assert.Null(result.Resume);
            Assert.Empty(result.ForeignPredictionIds);
            Assert.Equal(new[] { "pred-a" }, result.OrphanedPredictionIds);
            Assert.Equal(PredictionLedgerState.Open, saved.State);
            Assert.Equal(PredictionIntent.None, saved.Intent);
            Assert.Null(saved.WinningTeam);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public async Task Open_NewPrediction_PersistsReplayAttemptKey()
    {
        string directory = NewDirectory();
        try
        {
            var http = new PredictionHttp();
            TwitchMatchPredictionService service = Service(directory, http);

            bool opened = await service.OpenAsync(10, "Cursed Hollow", CancellationToken.None);
            PredictionLedgerEntry saved = PredictionLedger
                .Load(PredictionLedger.PathFor(directory))
                .FindByPredictionId("created-prediction");

            Assert.True(opened);
            Assert.Equal(1, http.CreateCount);
            Assert.Equal("10:1", saved.SessionKey);
            Assert.Equal(10, saved.ReplayId);
            Assert.Equal(1, saved.Attempt);
            Assert.Equal("Cursed Hollow: who wins?", saved.Title);
            Assert.Equal(BlueOutcomeId, saved.BlueOutcomeId);
            Assert.Equal(RedOutcomeId, saved.RedOutcomeId);
            Assert.Equal(PredictionLedgerState.Open, saved.State);
            Assert.NotEqual(saved.Title, saved.SessionKey);
        }
        finally
        {
            Delete(directory);
        }
    }

    private static TwitchMatchPredictionService Service(string directory, PredictionHttp http) =>
        new TwitchMatchPredictionService(
            NullLogger<TwitchMatchPredictionService>.Instance,
            new AppSettings
            {
                Location = new LocationSettings { DataDirectory = directory },
                Twitch = new TwitchSettings
                {
                    EnablePredictions = true,
                    DryRunMode = false,
                    Channel = "saltysadism",
                    PredictionWindow = TimeSpan.FromMinutes(2),
                },
                Capture = new CaptureSettings { Method = CaptureMethod.BitBlt },
            },
            FakeTwitchApi.Create(http),
            new PredictionReportWriter(
                NullLogger<PredictionReportWriter>.Instance,
                new AppSettings()
            )
        );

    private static void Seed(
        string directory,
        int replayId,
        int attempt,
        string predictionId,
        string title
    )
    {
        PredictionLedger
            .Load(PredictionLedger.PathFor(directory))
            .Upsert(
                new PredictionLedgerEntry
                {
                    SessionKey = replayId + ":" + attempt,
                    ReplayId = replayId,
                    Attempt = attempt,
                    PredictionId = predictionId,
                    BlueOutcomeId = BlueOutcomeId,
                    RedOutcomeId = RedOutcomeId,
                    BroadcasterId = BroadcasterId,
                    Title = title,
                    Map = "Cursed Hollow",
                    CreatedAt = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero),
                    State = PredictionLedgerState.Open,
                }
            );
    }

    private static string NewDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hr-ledger-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Delete(string directory)
    {
        try
        {
            Directory.Delete(directory, true);
        }
        catch (IOException) { }
    }

    private static RemoteItem Item(string id, string title, string status) =>
        new RemoteItem
        {
            Id = id,
            Title = title,
            Status = status,
        };

    private sealed class RemoteItem
    {
        public string Id;
        public string Title;
        public string Status;
    }

    private sealed class PredictionHttp : IHttpCallHandler
    {
        public List<RemoteItem> Items { get; } = new List<RemoteItem>();
        public bool FailPatch { get; set; }
        public int CreateCount { get; private set; }
        public int PatchCount { get; private set; }
        public string LastPatch { get; private set; }
        public string CreatedId { get; set; } = "created-prediction";

        public Task<KeyValuePair<int, string>> GeneralRequestAsync(
            string url,
            string method,
            string payload = null,
            ApiVersion api = ApiVersion.Helix,
            string clientId = null,
            string accessToken = null
        )
        {
            if (url.Contains("/users", StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new KeyValuePair<int, string>(
                        200,
                        "{\"data\":[{\"id\":\"" + BroadcasterId + "\",\"login\":\"saltysadism\"}]}"
                    )
                );
            }

            if (url.Contains("/predictions", StringComparison.Ordinal) && method == "GET")
            {
                return Task.FromResult(new KeyValuePair<int, string>(200, Body(Items)));
            }

            if (url.Contains("/predictions", StringComparison.Ordinal) && method == "PATCH")
            {
                PatchCount++;
                LastPatch = payload;
                if (FailPatch)
                {
                    throw new InternalServerErrorException("temporary");
                }

                string status =
                    payload != null && payload.Contains("CANCELED", StringComparison.Ordinal)
                        ? "CANCELED"
                        : "RESOLVED";
                foreach (RemoteItem item in Items)
                {
                    if (payload != null && payload.Contains(item.Id, StringComparison.Ordinal))
                    {
                        item.Status = status;
                    }
                }

                return Task.FromResult(new KeyValuePair<int, string>(200, Body(Items)));
            }

            if (url.Contains("/predictions", StringComparison.Ordinal) && method == "POST")
            {
                CreateCount++;
                var created = new RemoteItem
                {
                    Id = CreatedId,
                    Title = "Cursed Hollow: who wins?",
                    Status = "ACTIVE",
                };
                Items.Add(created);
                return Task.FromResult(new KeyValuePair<int, string>(200, Body(new[] { created })));
            }

            return Task.FromResult(new KeyValuePair<int, string>(200, "{}"));
        }

        public Task PutBytesAsync(string url, byte[] payload) => Task.CompletedTask;

        public Task<int> RequestReturnResponseCodeAsync(
            string url,
            string method,
            List<KeyValuePair<string, string>> getParams = null
        ) => Task.FromResult(200);

        private static string Body(IEnumerable<RemoteItem> items)
        {
            var parts = new List<string>();
            foreach (RemoteItem item in items)
            {
                parts.Add(
                    "{"
                        + "\"id\":\""
                        + item.Id
                        + "\","
                        + "\"broadcaster_id\":\""
                        + BroadcasterId
                        + "\","
                        + "\"title\":\""
                        + item.Title
                        + "\","
                        + "\"status\":\""
                        + item.Status
                        + "\","
                        + "\"outcomes\":["
                        + "{\"id\":\""
                        + BlueOutcomeId
                        + "\",\"title\":\"Blue\"},"
                        + "{\"id\":\""
                        + RedOutcomeId
                        + "\",\"title\":\"Red\"}"
                        + "]}"
                );
            }

            return "{\"data\":[" + string.Join(",", parts) + "]}";
        }
    }

    private class FakeTwitchApi : DispatchProxy
    {
        private Helix helix;

        public static ITwitchAPI Create(IHttpCallHandler http)
        {
            object created = DispatchProxy.Create<ITwitchAPI, FakeTwitchApi>();
            var proxy = (FakeTwitchApi)created;
            proxy.helix = new Helix(
                settings: new TwitchLib.Api.Core.ApiSettings
                {
                    ClientId = "test-client",
                    AccessToken = "test-token",
                },
                http: http
            );
            return (ITwitchAPI)created;
        }

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod.Name == "get_Helix")
            {
                return helix;
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }
}
