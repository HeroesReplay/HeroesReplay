using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Context;
using HeroesReplay.Core.Services.HeroesProfileExtension;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfileExtension;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ExtensionSnapshotSelectorTests
{
    [Fact]
    public void Select_LobbyHidesHeroesAndTalents()
    {
        ExtensionSnapshot snapshot = ExtensionSnapshotSelector.Select(
            Sample(),
            ExtensionSnapshotSelector.Lobby,
            TimeSpan.Zero
        );

        Assert.Equal("lobby", snapshot.Phase);
        Assert.All(snapshot.Players, player => Assert.Null(player.Hero));
        Assert.All(snapshot.Players, player => Assert.Null(player.HeroAttribute));
        Assert.All(snapshot.Players, player => Assert.Empty(player.Talents));
    }

    [Fact]
    public void Select_InGameIncludesPicksAtOrBeforeTheClock()
    {
        ExtensionSnapshot snapshot = ExtensionSnapshotSelector.Select(
            Sample(),
            ExtensionSnapshotSelector.InGame,
            TimeSpan.FromMinutes(3)
        );

        Assert.Equal(new[] { "A", "B" }, snapshot.Players[0].Talents);
        Assert.Empty(snapshot.Players[1].Talents);
        Assert.Equal("Abathur", snapshot.Players[0].Hero);
    }

    [Fact]
    public void Select_EndedIncludesEveryPick()
    {
        ExtensionSnapshot snapshot = ExtensionSnapshotSelector.Select(
            Sample(),
            ExtensionSnapshotSelector.Ended,
            timer: null
        );

        Assert.Equal(new[] { "A", "B" }, snapshot.Players[0].Talents);
        Assert.Equal(new[] { "C" }, snapshot.Players[1].Talents);
    }

    [Fact]
    public void Hash_IgnoresGameIdAndSeq()
    {
        string hash = ExtensionSnapshotSelector.Hash(
            ExtensionSnapshotSelector.Select(Sample(), ExtensionSnapshotSelector.Lobby, null)
        );

        Assert.Equal(
            hash,
            ExtensionSnapshotSelector.Hash(
                ExtensionSnapshotSelector.Select(Sample(), ExtensionSnapshotSelector.Lobby, null)
            )
        );
        Assert.DoesNotContain("game_id", hash);
        Assert.DoesNotContain("\"seq\"", hash);
    }

    private static ExtensionGame Sample()
    {
        return new ExtensionGame
        {
            GameMode = "StormLeague",
            Map = "Cursed Hollow",
            GameVersion = "2.55.17.98025",
            Players = new[]
            {
                new ExtensionPlayer("Sam", 100, 1, 0, "Abathur", "Abat", false),
                new ExtensionPlayer("Elite", 0, 1, 1, "Lili", "Lili", true),
            },
            Talents = new[]
            {
                new ExtensionTalentPick(TimeSpan.FromMinutes(1), 0, "A"),
                new ExtensionTalentPick(TimeSpan.FromMinutes(3), 0, "B"),
                new ExtensionTalentPick(TimeSpan.FromMinutes(4), 1, "C"),
            },
        };
    }
}

[Trait(TestCategories.Category, TestCategories.Unit)]
public class TwitchExtensionServiceTests
{
    [Fact]
    public async Task PostSnapshot_SendsSnakeCaseJsonAndStopsOnUnauthorized()
    {
        var handler = new ScriptedHandler(
            Json(HttpStatusCode.Unauthorized, "{\"message\":\"That key is not valid.\"}")
        );
        TwitchExtensionService service = Service(handler, "uploader-key");

        ExtensionPostOutcome outcome = await service.PostSnapshotAsync(
            "game-1",
            1,
            ExtensionSnapshotSelector.Select(Game(), ExtensionSnapshotSelector.Lobby, null)
        );

        Assert.Equal(ExtensionPostOutcome.Stopped, outcome);
        Assert.Single(handler.Captured);
        Assert.Equal(
            "https://www.heroesprofile.com/api/twitch/v1/uploader/snapshot",
            handler.Captured[0].Url
        );
        Assert.Equal("uploader-key", handler.Captured[0].Key);
        Assert.Contains("\"game_id\":\"game-1\"", handler.Captured[0].Body);
        Assert.Contains("\"hero_attribute\":null", handler.Captured[0].Body);
        Assert.Contains("\"battletag\":100", handler.Captured[0].Body);
    }

    [Fact]
    public async Task PostSnapshot_RetriesRateLimitWithTheSameBody()
    {
        var limited = new HttpResponseMessage((HttpStatusCode)429);
        limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(20));
        var handler = new ScriptedHandler(limited, new HttpResponseMessage(HttpStatusCode.OK));
        TwitchExtensionService service = Service(handler, "uploader-key");
        ExtensionSnapshot snapshot = ExtensionSnapshotSelector.Select(
            Game(),
            ExtensionSnapshotSelector.InGame,
            TimeSpan.FromMinutes(1)
        );

        ExtensionPostOutcome outcome = await service.PostSnapshotAsync("game-1", 2, snapshot);

        Assert.Equal(ExtensionPostOutcome.Sent, outcome);
        Assert.Equal(2, handler.Captured.Count);
        Assert.Equal(handler.Captured[0].Body, handler.Captured[1].Body);
    }

    [Fact]
    public async Task WhoAmI_ReadsTheChannelAndEntitlement()
    {
        var handler = new ScriptedHandler(
            Json(
                HttpStatusCode.OK,
                "{\"twitch_login\":\"saltysadism\",\"twitch_display_name\":\"SaltySadism\",\"player_linked\":false,\"entitlement\":{\"active\":true}}"
            )
        );

        ExtensionWhoAmI who = await Service(handler, "uploader-key").WhoAmIAsync();

        Assert.True(who.Reachable);
        Assert.Equal("SaltySadism", who.TwitchDisplayName);
        Assert.True(who.EntitlementActive);
        Assert.False(who.PlayerLinked);
        Assert.Equal(
            "https://www.heroesprofile.com/api/twitch/v1/uploader/whoami",
            handler.Captured[0].Url
        );
    }

    [Fact]
    public async Task WhoAmI_MissingKeyDoesNotCallHeroesProfile()
    {
        var handler = new ScriptedHandler();

        ExtensionWhoAmI who = await Service(handler, apiKey: null).WhoAmIAsync();

        Assert.False(who.Reachable);
        Assert.Empty(handler.Captured);
    }

    private static TwitchExtensionService Service(ScriptedHandler handler, string apiKey)
    {
        return new TwitchExtensionService(
            NullLogger<TwitchExtensionService>.Instance,
            new HttpClient(handler),
            new AppSettings
            {
                HeroesProfileApi = new HeroesProfileApiSettings
                {
                    TwitchBaseUri = new Uri("https://www.heroesprofile.com/api/twitch/v1/"),
                },
                TwitchExtension = new HeroesProfileTwitchExtensionSettings { ApiKey = apiKey },
            }
        );
    }

    private static ExtensionGame Game()
    {
        return new ExtensionGame
        {
            GameMode = "StormLeague",
            Map = "Cursed Hollow",
            GameVersion = "2.55",
            Players = new[] { new ExtensionPlayer("Sam", 100, 1, 0, "Abathur", "Abat", false) },
            Talents = new[] { new ExtensionTalentPick(TimeSpan.FromMinutes(1), 0, "A") },
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses;

        public ScriptedHandler(params HttpResponseMessage[] responses)
        {
            this.responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<Captured> Captured { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string body =
                request.Content == null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);
            Captured.Add(
                new Captured(
                    request.RequestUri?.ToString(),
                    request.Headers.TryGetValues(
                        TwitchExtensionService.UploaderKeyHeader,
                        out IEnumerable<string> keys
                    )
                        ? keys.Single()
                        : null,
                    body
                )
            );
            return responses.Dequeue();
        }
    }

    private sealed record Captured(string Url, string Key, string Body);
}

[Trait(TestCategories.Category, TestCategories.Unit)]
public class TalentNotifierTests
{
    [Fact]
    public async Task Send_WaitsForTheIntervalAndEndGameBypassesIt()
    {
        var extension = new RecordingExtension();
        var notifier = new TalentNotifier(
            NullLogger<TalentNotifier>.Instance,
            new FixedContext(Game()),
            extension,
            new AppSettings
            {
                TwitchExtension = new HeroesProfileTwitchExtensionSettings
                {
                    MinInterval = TimeSpan.FromHours(1),
                },
            }
        );
        notifier.ClearSession();

        await notifier.SendCurrentTalentsAsync(TimeSpan.Zero, clockLive: false);
        await notifier.SendCurrentTalentsAsync(TimeSpan.FromMinutes(5), clockLive: true);
        await notifier.EndGameAsync();

        Assert.Equal(2, extension.Posts.Count);
        Assert.Equal("lobby", extension.Posts[0].Snapshot.Phase);
        Assert.Null(extension.Posts[0].Snapshot.Players[0].Hero);
        Assert.Equal(1, extension.Posts[0].Seq);
        Assert.Equal("ended", extension.Posts[1].Snapshot.Phase);
        Assert.Equal(new[] { "A", "Late" }, extension.Posts[1].Snapshot.Players[0].Talents);
        Assert.Equal(2, extension.Posts[1].Seq);
        Assert.Equal(extension.Posts[0].GameId, extension.Posts[1].GameId);
    }

    [Fact]
    public async Task Send_StopsAfterUnauthorized()
    {
        var extension = new RecordingExtension { Next = ExtensionPostOutcome.Stopped };
        var notifier = new TalentNotifier(
            NullLogger<TalentNotifier>.Instance,
            new FixedContext(Game()),
            extension,
            new AppSettings
            {
                TwitchExtension = new HeroesProfileTwitchExtensionSettings
                {
                    MinInterval = TimeSpan.FromSeconds(8),
                },
            }
        );
        notifier.ClearSession();

        await notifier.SendCurrentTalentsAsync(TimeSpan.Zero, clockLive: false);
        await notifier.SendCurrentTalentsAsync(TimeSpan.FromMinutes(1), clockLive: true);
        await notifier.EndGameAsync();

        Assert.Single(extension.Posts);
    }

    private static ExtensionGame Game()
    {
        return new ExtensionGame
        {
            GameMode = "StormLeague",
            Map = "Cursed Hollow",
            GameVersion = "2.55",
            Players = new[] { new ExtensionPlayer("Sam", 100, 1, 0, "Abathur", "Abat", false) },
            Talents = new[]
            {
                new ExtensionTalentPick(TimeSpan.FromMinutes(1), 0, "A"),
                new ExtensionTalentPick(TimeSpan.FromMinutes(20), 0, "Late"),
            },
        };
    }

    private sealed class FixedContext : IReplayContext
    {
        public FixedContext(ExtensionGame game)
        {
            Current = new ContextData { Payloads = game };
        }

        public ContextData Previous => null;
        public ContextData Current { get; }
    }

    private sealed class RecordingExtension : ITwitchExtensionService
    {
        public List<(string GameId, int Seq, ExtensionSnapshot Snapshot)> Posts { get; } = new();
        public ExtensionPostOutcome Next { get; set; } = ExtensionPostOutcome.Sent;

        public Task<ExtensionPostOutcome> PostSnapshotAsync(
            string gameId,
            int seq,
            ExtensionSnapshot snapshot,
            CancellationToken token = default
        )
        {
            Posts.Add((gameId, seq, snapshot));
            return Task.FromResult(Next);
        }

        public Task<ExtensionWhoAmI> WhoAmIAsync(CancellationToken token = default)
        {
            throw new NotSupportedException();
        }
    }
}
