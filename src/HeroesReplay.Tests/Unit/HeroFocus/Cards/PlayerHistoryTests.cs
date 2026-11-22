using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Targeted;
using HeroesReplay.Core.YouTube.Metadata;
using HeroesReplay.Tests.Unit.HeroesProfile;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static HeroesReplay.Tests.Unit.HeroFocus.FocusFixtures;

namespace HeroesReplay.Tests.Unit.HeroFocus.Cards;

/// <summary>The players' match history before a match, for title stories.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class PlayerHistoryTests : IDisposable
{
    private static readonly DateTime Played = new(2026, 11, 15, 19, 33, 52, DateTimeKind.Utc);

    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "hr-player-history-" + Guid.NewGuid().ToString("N")
    );
    private readonly FakeHeroesProfileApi api = new();

    public void Dispose() => TestTemp.Delete(folder);

    [Fact]
    public void Parse_ReadsTheHeroAndTheMmrChange()
    {
        const string body =
            "{\"current_page\":1,\"per_page\":100,\"data\":[{\"replayID\":66698689,\"game_date\":\"2026-11-15 19:33:52\",\"winner\":1,\"kills\":3,\"deaths\":2,\"assists\":9,\"player_change\":2.61,\"hero\":{\"name\":\"Azmodan\",\"attribute_id\":\"Azmo\"}}]}";

        PlayerMatch match = PlayerMatches.Parse(body).Single();

        Assert.Equal("Azmodan", match.Hero);
        Assert.Equal("Azmo", match.AttributeId);
        Assert.Equal(2.61, match.MmrChange);
        Assert.True(match.Won);
    }

    [Fact]
    public void From_CountsOnlyGamesBeforeTheMatch_NeverTheMatchItself()
    {
        var matches = new List<PlayerMatch>
        {
            // Later the same day, and the match itself: both left out, so the result never leaks.
            Game(10, Played.AddMinutes(40), won: false),
            Game(9, Played, won: true),
            Game(8, Played.AddMinutes(-25), won: true),
            Game(7, Played.AddMinutes(-50), won: true),
            Game(6, Played.AddMinutes(-80), won: true),
            Game(5, Played.AddMinutes(-110), won: false),
        };

        PlayerHistory history = PlayerHistoryMath.From(
            matches,
            9,
            Played,
            "Azmodan",
            "Azmo",
            new PlayerStorySettings(),
            Played
        );

        Assert.Equal(PlayerHistoryStatus.Known, history.Status);
        Assert.Equal(4, history.Games);
        Assert.Equal(3, history.Streak);
        Assert.Equal(4, history.MmrGames);
        Assert.Equal(3 * 3.0 - 3.0, history.MmrChange);
    }

    [Fact]
    public void From_ALossStreakIsNegative_AndOldGamesAreOutsideTheLookback()
    {
        var matches = new List<PlayerMatch>
        {
            Game(3, Played.AddHours(-1), won: false),
            Game(2, Played.AddHours(-2), won: false),
            Game(1, Played.AddDays(-90), won: false),
        };

        PlayerHistory history = PlayerHistoryMath.From(
            matches,
            9,
            Played,
            "Azmodan",
            "Azmo",
            new PlayerStorySettings { LookbackDays = 60 },
            Played
        );

        Assert.Equal(2, history.Games);
        Assert.Equal(-2, history.Streak);
    }

    [Fact]
    public void From_FindsTheBestAndWorstHeroLately()
    {
        var matches = new List<PlayerMatch>();
        int id = 100;
        DateTime at = Played.AddHours(-1);
        void Add(string hero, int wins, int losses)
        {
            for (int i = 0; i < wins + losses; i++)
            {
                matches.Add(Game(id++, at, won: i < wins, hero: hero));
                at = at.AddMinutes(-30);
            }
        }

        Add("Gall", 2, 8);
        Add("Tyrael", 7, 3);
        Add("Nova", 5, 5);
        var settings = new PlayerStorySettings { LatelyMinGames = 8 };

        PlayerHistory gall = PlayerHistoryMath.From(matches, 1, Played, "Gall", "Gall", settings, Played);
        PlayerHistory tyrael = PlayerHistoryMath.From(matches, 1, Played, "Tyrael", "Tyrael", settings, Played);
        PlayerHistory nova = PlayerHistoryMath.From(matches, 1, Played, "Nova", "Nova", settings, Played);

        Assert.True(gall.WorstHero);
        Assert.False(gall.BestHero);
        Assert.Equal(10, gall.HeroGames);
        Assert.Equal(2, gall.HeroWins);
        Assert.True(tyrael.BestHero);
        Assert.False(nova.BestHero || nova.WorstHero);
    }

    [Fact]
    public async Task Refresh_FillsEachHumanPlayer_AsksUpToTheMatchDate()
    {
        api.Answer(
            PlayerMatches.Path,
            FakeHeroesProfileApi.Ok(
                "{\"data\":[{\"replayID\":5,\"game_date\":\"2026-11-15 18:00:00\",\"winner\":1,\"player_change\":3.2,\"hero\":{\"name\":\"Valla\",\"attribute_id\":\"Demo\"}}]}"
            )
        );
        FocusCard card = Card(9, Player(0, "Valla", "Demo", "Ranged Assassin"));
        card.GameDateUtc = Played;
        card.Players[9].IsAi = true;

        PlayerHistoryRefresh.FillResult result = await Refresh(new PlayerStorySettings { Enabled = true })
            .FillAsync(card, 20, CancellationToken.None);

        Assert.Equal(9, result.Calls);
        Assert.True(result.Changed);
        Assert.Equal("2026-11-15", api.Calls[0].Query["end_date"]);
        Assert.Equal("2026-09-16", api.Calls[0].Query["start_date"]);
        Assert.Equal("sl", api.Calls[0].Query["game_type"]);
        PlayerHistory star = card.Players[0].History;
        Assert.True(star.IsKnown);
        Assert.Equal(1, star.Streak);
        Assert.Equal(1, star.HeroGames);
        Assert.Null(card.Players[9].History);
    }

    [Fact]
    public async Task Refresh_MarksAPrivatePlayer_AndStopsOnARefusedKey()
    {
        api.Answer(
            PlayerMatches.Path,
            FakeHeroesProfileApi.Error(403, "player_unavailable"),
            FakeHeroesProfileApi.Error(401)
        );
        FocusCard card = Card(9, Player(0, "Valla", "Demo", "Ranged Assassin"));
        card.GameDateUtc = Played;
        PlayerHistoryRefresh refresh = Refresh(new PlayerStorySettings { Enabled = true });

        PlayerHistoryRefresh.FillResult result = await refresh.FillAsync(card, 20, CancellationToken.None);

        Assert.Equal(PlayerHistoryStatus.Private, card.Players[0].History.Status);
        Assert.True(result.Stop);
        Assert.True(refresh.Stopped);
        Assert.Equal(2, result.Calls);
    }

    [Fact]
    public async Task Refresh_DoesNothingWhenStoriesAreOff()
    {
        var cards = new FocusCardStore(Path.Combine(folder, "cards"));
        FocusCard card = Card(9, Player(0, "Valla", "Demo", "Ranged Assassin"));
        card.GameDateUtc = Played;
        cards.Write(card);

        int calls = await Refresh(new PlayerStorySettings())
            .StepAsync(cards, new[] { "9" }, CancellationToken.None);

        Assert.Equal(0, calls);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public void Source_LeavesOutPrivateAndUnreadPlayers()
    {
        FocusCard card = Card(9, Player(0, "Valla", "Demo", "Ranged Assassin"));
        card.Players[0].History = new PlayerHistory { Status = PlayerHistoryStatus.Known, Games = 30, Streak = 6 };
        card.Players[1].History = new PlayerHistory { Status = PlayerHistoryStatus.Private };
        card.Players[2].History = new PlayerHistory
        {
            Status = PlayerHistoryStatus.Known,
            Games = 30,
        };
        card.Players[2].Strength = new FocusStrength { Status = FocusStrengthStatus.Private };

        PlayerStoryFacts only = PlayerStorySource.From(card).Single();

        Assert.Equal("Valla", only.Hero);
        Assert.Equal(6, only.History.Streak);
    }

    private PlayerHistoryRefresh Refresh(PlayerStorySettings settings) =>
        new(
            new HeroesProfileCaller(api, api.Pacer(), NullLogger.Instance, api.Clock, api.Delay),
            new PlayerHeroStatsStore(Path.Combine(folder, "players")),
            settings,
            TimeSpan.FromMinutes(1),
            NullLogger.Instance,
            () => api.Now.UtcDateTime
        );

    private static PlayerMatch Game(int id, DateTime at, bool won, string hero = "Azmodan") =>
        new(id, at, won, 1, 1, 1, hero, hero == "Azmodan" ? "Azmo" : hero, won ? 3.0 : -3.0);
}
