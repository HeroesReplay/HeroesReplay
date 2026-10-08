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
using TwitchLib.Api.Core.Interfaces;
using TwitchLib.Api.Helix;
using TwitchLib.Api.Interfaces;
using TwitchLib.Client.Interfaces;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch.Predictions;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class TwitchMatchPredictionServiceTests
{
    private const string PredictionId = "530927bb-7b16-47e1-82bb-7eedafda11aa";
    private const string BlueOutcomeId = "blue-outcome";
    private const string RedOutcomeId = "red-outcome";
    private const string BroadcasterId = "487238352";

    [Fact]
    public async Task ResolveTeam_LockedChannelPrediction_ResolvesBlueOutcome()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hr-pred-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        PredictionLedger
            .Load(PredictionLedger.PathFor(directory))
            .Upsert(
                new PredictionLedgerEntry
                {
                    SessionKey = "10:1",
                    ReplayId = 10,
                    Attempt = 1,
                    PredictionId = PredictionId,
                    BlueOutcomeId = BlueOutcomeId,
                    RedOutcomeId = RedOutcomeId,
                    BroadcasterId = BroadcasterId,
                    Title = "Cursed Hollow: who wins?",
                    Map = "Cursed Hollow",
                    CreatedAt = DateTimeOffset.UtcNow,
                    State = PredictionLedgerState.Open,
                }
            );
        var http = new RecordingHttpHandler();
        http.LockedTitle = "Not the map title";
        ITwitchAPI api = FakeTwitchApi.Create(http);
        var service = new TwitchMatchPredictionService(
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
            api,
            new PredictionReportWriter(
                NullLogger<PredictionReportWriter>.Instance,
                new AppSettings()
            )
        );

        try
        {
            await service.OpenAsync(10, "Cursed Hollow", CancellationToken.None);
            await service.ResolveTeamAsync(0, CancellationToken.None);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException) { }
        }

        Assert.NotNull(http.EndPredictionBody);
        Assert.Contains("\"RESOLVED\"", http.EndPredictionBody, StringComparison.Ordinal);
        Assert.Contains(PredictionId, http.EndPredictionBody, StringComparison.Ordinal);
        Assert.Contains(BlueOutcomeId, http.EndPredictionBody, StringComparison.Ordinal);
        Assert.DoesNotContain(RedOutcomeId, http.EndPredictionBody, StringComparison.Ordinal);
        Assert.Equal(0, http.CreateCount);
    }

    [Fact]
    public async Task Open_LockedPredictionForAnotherMap_CancelsItInsteadOfResolving()
    {
        var http = new RecordingHttpHandler();
        http.LockedTitle = "Towers of Doom: who wins?";
        ITwitchAPI api = FakeTwitchApi.Create(http);
        var service = new TwitchMatchPredictionService(
            NullLogger<TwitchMatchPredictionService>.Instance,
            new AppSettings
            {
                Twitch = new TwitchSettings
                {
                    EnablePredictions = true,
                    DryRunMode = false,
                    Channel = "saltysadism",
                    PredictionWindow = TimeSpan.FromMinutes(2),
                },
                Capture = new CaptureSettings { Method = CaptureMethod.BitBlt },
            },
            api,
            new PredictionReportWriter(
                NullLogger<PredictionReportWriter>.Instance,
                new AppSettings()
            )
        );

        await service.OpenAsync(11, "Garden of Terror", CancellationToken.None);

        Assert.NotNull(http.EndPredictionBody);
        Assert.Contains("\"CANCELED\"", http.EndPredictionBody, StringComparison.Ordinal);
        Assert.DoesNotContain("\"RESOLVED\"", http.EndPredictionBody, StringComparison.Ordinal);
        Assert.Contains(PredictionId, http.EndPredictionBody, StringComparison.Ordinal);
        Assert.Equal(1, http.CreateCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResolveReplay_ResolvedByTwitch_PostsTheVerdictToChatWhenTheBotIsOn(
        bool chatBot
    )
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "hr-pred-chat-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        PredictionLedger
            .Load(PredictionLedger.PathFor(directory))
            .Upsert(
                new PredictionLedgerEntry
                {
                    SessionKey = "10:1",
                    ReplayId = 10,
                    Attempt = 1,
                    PredictionId = PredictionId,
                    BlueOutcomeId = BlueOutcomeId,
                    RedOutcomeId = RedOutcomeId,
                    BroadcasterId = BroadcasterId,
                    Title = "Cursed Hollow: who wins?",
                    Map = "Cursed Hollow",
                    CreatedAt = DateTimeOffset.UtcNow,
                    State = PredictionLedgerState.Open,
                }
            );
        var http = new RecordingHttpHandler { ResolvedBody = ResolvedBlue() };
        var chat = ChatRecorder.Create();
        var settings = new AppSettings
        {
            Location = new LocationSettings { DataDirectory = directory },
            Twitch = new TwitchSettings
            {
                EnablePredictions = true,
                EnableChatBot = chatBot,
                DryRunMode = false,
                Channel = "saltysadism",
                PredictionWindow = TimeSpan.FromMinutes(2),
            },
            Capture = new CaptureSettings { Method = CaptureMethod.BitBlt },
        };
        var service = new TwitchMatchPredictionService(
            NullLogger<TwitchMatchPredictionService>.Instance,
            settings,
            FakeTwitchApi.Create(http),
            new PredictionReportWriter(NullLogger<PredictionReportWriter>.Instance, settings),
            chat
        );

        string saved;
        try
        {
            await service.ResolveReplayAsync(10, 0, CancellationToken.None);
            saved = File.ReadAllText(
                Path.Combine(directory, PredictionReportWriter.SavedReportFileName)
            );
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException) { }
        }

        PredictionReport report = System.Text.Json.JsonSerializer.Deserialize<PredictionReport>(
            saved
        );
        List<(string Channel, string Message)> sent = ((ChatRecorder)(object)chat).Sent;
        Assert.Contains("\"RESOLVED\"", http.EndPredictionBody, StringComparison.Ordinal);
        Assert.Equal("Cursed Hollow", report.Map);
        Assert.Equal(PredictionVerdictKind.Upset, PredictionVerdict.Kind(report));
        Assert.False(string.IsNullOrWhiteSpace(report.Verdict));
        if (chatBot)
        {
            (string channel, string message) = Assert.Single(sent);
            Assert.Equal("saltysadism", channel);
            Assert.Equal(report.Verdict, message);
            Assert.True(message.Length <= PredictionVerdict.ChatLimit);
        }
        else
        {
            Assert.Empty(sent);
        }
    }

    private static string ResolvedBlue() =>
        "{\"data\":[{"
        + "\"id\":\""
        + PredictionId
        + "\",\"broadcaster_id\":\""
        + BroadcasterId
        + "\",\"title\":\"Cursed Hollow: who wins?\",\"status\":\"RESOLVED\","
        + "\"winning_outcome_id\":\""
        + BlueOutcomeId
        + "\",\"outcomes\":["
        + "{\"id\":\""
        + BlueOutcomeId
        + "\",\"title\":\"Blue\",\"users\":2,\"channel_points\":1500,\"top_predictors\":["
        + "{\"user_id\":\"1\",\"user_name\":\"Ana\",\"user_login\":\"ana\",\"channel_points_used\":1000,\"channel_points_won\":4000}]},"
        + "{\"id\":\""
        + RedOutcomeId
        + "\",\"title\":\"Red\",\"users\":5,\"channel_points\":4500,\"top_predictors\":["
        + "{\"user_id\":\"3\",\"user_name\":\"Cy\",\"user_login\":\"cy\",\"channel_points_used\":3000,\"channel_points_won\":0}]}"
        + "]}]}";

    private class ChatRecorder : DispatchProxy
    {
        public List<(string Channel, string Message)> Sent { get; } = new();

        public static ITwitchClient Create() => DispatchProxy.Create<ITwitchClient, ChatRecorder>();

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (
                targetMethod.Name == nameof(ITwitchClient.SendMessage)
                && args.Length == 3
                && args[0] is string channel
            )
            {
                Sent.Add((channel, (string)args[1]));
            }

            return targetMethod.ReturnType.IsValueType && targetMethod.ReturnType != typeof(void)
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }

    private sealed class RecordingHttpHandler : IHttpCallHandler
    {
        public string EndPredictionBody { get; private set; }
        public int CreateCount { get; private set; }
        public string LockedTitle { get; set; } = "Cursed Hollow: who wins?";
        public string ResolvedBody { get; set; }

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
                return Task.FromResult(
                    new KeyValuePair<int, string>(
                        200,
                        "{\"data\":[{"
                            + "\"id\":\""
                            + PredictionId
                            + "\","
                            + "\"broadcaster_id\":\""
                            + BroadcasterId
                            + "\","
                            + "\"title\":\""
                            + LockedTitle
                            + "\","
                            + "\"status\":\"LOCKED\","
                            + "\"outcomes\":["
                            + "{\"id\":\""
                            + BlueOutcomeId
                            + "\",\"title\":\"Blue\"},"
                            + "{\"id\":\""
                            + RedOutcomeId
                            + "\",\"title\":\"Red\"}"
                            + "]}]}"
                    )
                );
            }

            if (url.Contains("/predictions", StringComparison.Ordinal) && method == "PATCH")
            {
                EndPredictionBody = payload;
                return Task.FromResult(
                    new KeyValuePair<int, string>(200, ResolvedBody ?? "{\"data\":[]}")
                );
            }

            if (url.Contains("/predictions", StringComparison.Ordinal) && method == "POST")
            {
                CreateCount++;
                return Task.FromResult(new KeyValuePair<int, string>(200, "{\"data\":[]}"));
            }

            return Task.FromResult(new KeyValuePair<int, string>(200, "{}"));
        }

        public Task PutBytesAsync(string url, byte[] payload) => Task.CompletedTask;

        public Task<int> RequestReturnResponseCodeAsync(
            string url,
            string method,
            List<KeyValuePair<string, string>> getParams = null
        ) => Task.FromResult(200);
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
