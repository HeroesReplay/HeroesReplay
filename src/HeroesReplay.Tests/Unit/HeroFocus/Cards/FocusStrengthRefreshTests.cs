using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.HeroFocus;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Tests.Unit.HeroesProfile;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static HeroesReplay.Tests.Unit.HeroFocus.FocusFixtures;

namespace HeroesReplay.Tests.Unit.HeroFocus.Cards;

/// <summary>The players' Heroes Profile records on the waiting cards (#433).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class FocusStrengthRefreshTests : IDisposable
{
    private const string Record =
        "[{\"wins\":30,\"losses\":20,\"games_played\":50,\"sl_rank_tier\":\"Gold 2\"}]";

    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "hr-focus-strength-" + Guid.NewGuid().ToString("N")
    );
    private readonly FakeHeroesProfileApi api = new();

    public FocusStrengthRefreshTests() =>
        api.Answer(PlayerHeroStatsFetch.PlayersHeroesPath, FakeHeroesProfileApi.Ok(Record));

    public void Dispose() => TestTemp.Delete(folder);

    [Fact]
    public async Task Fill_LooksUpTheBestKdaFirst_UpToTheLimit()
    {
        FocusCard card = Card(1, Player(0, "Valla", "Demo", "Ranged Assassin"));

        FocusStrengthRefresh.FillResult result = await Refresh()
            .FillAsync(card, 3, CancellationToken.None);

        Assert.Equal(3, result.Calls);
        Assert.True(result.Changed);
        Assert.False(result.Stop);
        Assert.Equal("Player0#1000", api.Calls[0].Query["battletag"]);
        FocusStrength star = card.Players[0].Strength;
        Assert.Equal(FocusStrengthStatus.Known, star.Status);
        Assert.Equal(30, star.Wins);
        Assert.Equal(50, star.Games);
        Assert.Equal("Gold 2", star.RankTier);
        Assert.Equal(3, card.Players.Count(player => player.Strength != null));
    }

    [Fact]
    public async Task Step_SpreadsItsLookupsOverTheCards_AndWritesThem()
    {
        var cards = new FocusCardStore(Path.Combine(folder, "cards"));
        cards.Write(Card(2, Player(0, "Valla", "Demo", "Ranged Assassin")));
        FocusCard other = Card(1, Player(0, "Raynor", "Rayn", "Ranged Assassin"));
        // Other players, so the first card's records do not answer this one's from the cache.
        other.Players.ForEach(player => player.BattleTag += 100);
        cards.Write(other);
        FocusStrengthRefresh refresh = Refresh(
            new HeroFocusSettings { StrengthLookupsPerStep = 12 }
        );

        int first = await refresh.StepAsync(cards, new[] { "2", "1" }, CancellationToken.None);
        int second = await refresh.StepAsync(cards, new[] { "2", "1" }, CancellationToken.None);
        int third = await refresh.StepAsync(cards, new[] { "2", "1" }, CancellationToken.None);

        Assert.Equal(12, first);
        Assert.Equal(8, second);
        Assert.Equal(0, third);
        Assert.All(cards.Read("1").Players, player => Assert.True(player.Strength.IsKnown));
        Assert.All(cards.Read("2").Players, player => Assert.True(player.Strength.IsKnown));
    }

    [Fact]
    public async Task Fill_StopsWhenTheWeeklyBudgetIsSpent()
    {
        FocusCard card = Card(1, Player(0, "Valla", "Demo", "Ranged Assassin"));
        FocusStrengthRefresh refresh = Refresh(
            players: new PlayerStatsSettings { WeeklyCallBudget = 2 }
        );

        FocusStrengthRefresh.FillResult result = await refresh.FillAsync(
            card,
            10,
            CancellationToken.None
        );

        Assert.True(result.Stop);
        Assert.Equal(2, api.Calls.Count);
        Assert.Equal(2, card.Players.Count(player => player.Strength != null));
    }

    [Fact]
    public async Task Fill_DropsTheNumbersOfAPlayerWhoWentPrivate()
    {
        FocusCard card = Card(
            1,
            Player(0, "Valla", "Demo", "Ranged Assassin", strength: Strength(30, 50))
        );
        card.Players[0].Strength.FetchedAtUtc = api.Now.UtcDateTime;
        PlayerHeroStatsStore store = Store();
        store.Write(
            "Player0#1000",
            2,
            "Demo",
            new PlayerHeroLookup(
                PlayerHeroLookupStatus.Known,
                new PlayerHeroStats(30, 20, 50, null, null, null)
            ),
            api.Now
        );
        store.MarkPrivate("Player0#1000", 2, api.Now);

        await Refresh().FillAsync(card, 0, CancellationToken.None);

        FocusStrength hidden = card.Players[0].Strength;
        Assert.Equal(FocusStrengthStatus.Private, hidden.Status);
        Assert.Equal(0, hidden.Games);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public void ARecord_IsReadAgainOnceItIsOld()
    {
        FocusStrengthRefresh refresh = Refresh();
        DateTime now = api.Now.UtcDateTime;

        Assert.True(refresh.IsDue(null));
        Assert.False(
            refresh.IsDue(
                new FocusStrength
                {
                    Status = FocusStrengthStatus.Known,
                    FetchedAtUtc = now.AddDays(-6),
                }
            )
        );
        Assert.True(
            refresh.IsDue(
                new FocusStrength
                {
                    Status = FocusStrengthStatus.Known,
                    FetchedAtUtc = now.AddDays(-7),
                }
            )
        );
        Assert.False(
            refresh.IsDue(
                new FocusStrength
                {
                    Status = FocusStrengthStatus.Private,
                    FetchedAtUtc = now.AddHours(-23),
                }
            )
        );
        Assert.True(
            refresh.IsDue(
                new FocusStrength
                {
                    Status = FocusStrengthStatus.Private,
                    FetchedAtUtc = now.AddDays(-1),
                }
            )
        );
        Assert.False(
            refresh.IsDue(
                new FocusStrength
                {
                    Status = FocusStrengthStatus.Unknown,
                    FetchedAtUtc = now.AddMinutes(-30),
                }
            )
        );
        Assert.True(
            refresh.IsDue(
                new FocusStrength
                {
                    Status = FocusStrengthStatus.Unknown,
                    FetchedAtUtc = now.AddHours(-1),
                }
            )
        );
    }

    [Theory]
    [InlineData(PlayerHeroLookupStatus.NoGames, FocusStrengthStatus.Known)]
    [InlineData(PlayerHeroLookupStatus.Private, FocusStrengthStatus.Private)]
    [InlineData(PlayerHeroLookupStatus.NotFound, FocusStrengthStatus.Unknown)]
    [InlineData(PlayerHeroLookupStatus.Unavailable, FocusStrengthStatus.Unknown)]
    public void Strength_MapsEachAnswer(PlayerHeroLookupStatus status, FocusStrengthStatus expected)
    {
        FocusStrength strength = FocusStrengthRefresh.Strength(new PlayerHeroLookup(status), Now);

        Assert.Equal(expected, strength.Status);
        Assert.Equal(0, strength.Games);
        Assert.Equal(Now, strength.FetchedAtUtc);
    }

    private PlayerHeroStatsStore Store() => new(Path.Combine(folder, "players"));

    private FocusStrengthRefresh Refresh(
        HeroFocusSettings focus = null,
        PlayerStatsSettings players = null
    )
    {
        players ??= new PlayerStatsSettings();
        var fetch = new PlayerHeroStatsFetch(
            new HeroesProfileCaller(api, api.Pacer(), NullLogger.Instance, api.Clock, api.Delay),
            Store(),
            players,
            NullLogger.Instance,
            api.Clock
        );
        return new FocusStrengthRefresh(
            fetch,
            focus ?? new HeroFocusSettings(),
            players,
            NullLogger.Instance,
            () => api.Now.UtcDateTime
        );
    }
}
