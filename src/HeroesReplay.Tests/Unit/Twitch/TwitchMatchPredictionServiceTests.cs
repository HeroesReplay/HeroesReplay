using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Twitch;
using Microsoft.Extensions.Logging.Abstractions;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Core.Interfaces;
using TwitchLib.Api.Helix;
using TwitchLib.Api.Interfaces;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

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
        var http = new RecordingHttpHandler();
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
            api
        );

        await service.OpenAsync("Cursed Hollow", CancellationToken.None);
        await service.ResolveTeamAsync(0, CancellationToken.None);

        Assert.NotNull(http.EndPredictionBody);
        Assert.Contains("\"RESOLVED\"", http.EndPredictionBody, StringComparison.Ordinal);
        Assert.Contains(PredictionId, http.EndPredictionBody, StringComparison.Ordinal);
        Assert.Contains(BlueOutcomeId, http.EndPredictionBody, StringComparison.Ordinal);
        Assert.DoesNotContain(RedOutcomeId, http.EndPredictionBody, StringComparison.Ordinal);
        Assert.Equal(0, http.CreateCount);
    }

    private sealed class RecordingHttpHandler : IHttpCallHandler
    {
        public string EndPredictionBody { get; private set; }
        public int CreateCount { get; private set; }

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
                            + "\"title\":\"Haunted Mines: who wins?\","
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
                return Task.FromResult(new KeyValuePair<int, string>(200, "{\"data\":[]}"));
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
