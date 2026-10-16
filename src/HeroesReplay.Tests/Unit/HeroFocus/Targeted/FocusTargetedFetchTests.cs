using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.HeroFocus;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Ledger;
using HeroesReplay.Core.HeroFocus.Targeted;
using HeroesReplay.Core.Shared;
using HeroesReplay.Tests.Unit.HeroesProfile;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroFocus.Targeted;

/// <summary>
/// The targeted fetch (#433): a hero neither focused nor seen for a week gets a strong player's
/// game, the leaderboard merged over several seasons names the player, and the step keeps to its
/// interval, its waiting cap and its weekly budget.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class FocusTargetedFetchTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<Hero> Roster = new[]
    {
        new Hero("Murky", "HeroMurky", null, "Murk", role: "Melee Assassin"),
        new Hero("Li-Ming", "HeroWizard", null, "Wiza", role: "Ranged Assassin"),
        new Hero("Uther", "HeroUther", null, "Uthe", role: "Healer"),
    };

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "hr-focus-targeted-" + Guid.NewGuid().ToString("N")
    );
    private readonly FakeHeroesProfileApi api = new();
    private readonly List<int> downloads = new();
    private readonly HeroFocusSettings settings = new() { Enabled = true };
    private DateTime now = Start;
    private Func<int, TargetedDownload> answer = _ => TargetedDownload.Downloaded;

    public void Dispose() => TestTemp.Delete(root);

    [Fact]
    public void TheLeaderboard_IsMergedOverSeasons_PerPlayer()
    {
        IReadOnlyList<LeaderboardPlayer> merged = HeroLeaderboard.Merge(
            new[]
            {
                HeroLeaderboard.Parse(Board(("Star#1", 2, 10, 70), ("Other#2", 1, 30, 50))),
                HeroLeaderboard.Parse(Board(("star#1", 2, 20, 55), ("Lone#3", 2, 5, 100))),
            }
        );

        LeaderboardPlayer star = merged.Single(player => player.BattleTag == "Star#1");
        Assert.Equal(30, star.Games);
        Assert.Equal(18, star.Wins, 3);
        Assert.Equal(new[] { "Star#1", "Other#2", "Lone#3" }, merged.Select(p => p.BattleTag));
        Assert.Equal(34, HeroLeaderboard.CurrentSeason(Patches(34)));
        Assert.Empty(HeroLeaderboard.Parse("not json"));
    }

    [Fact]
    public void TheMatches_AreTriedWonFirst_ThenByScore()
    {
        IReadOnlyList<PlayerMatch> best = PlayerMatches.Best(
            PlayerMatches.Parse(
                Matches(
                    (101, "2026-10-14 10:00:00", 0, 9, 0, 9),
                    (102, "2026-10-13 10:00:00", 1, 2, 4, 3),
                    (103, "2026-10-12 10:00:00", 1, 8, 1, 10)
                )
            )
        );

        Assert.Equal(new[] { 103, 102, 101 }, best.Select(match => match.ReplayId));
        Assert.Equal(new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc), best[0].PlayedAtUtc);
    }

    [Fact]
    public async Task NothingIsFetched_BeforeThePoolHasHadAWeek()
    {
        FocusTargetedFetch fetch = Fetch();

        Assert.Equal(TargetedStep.NoTarget, await fetch.StepAsync(null, CancellationToken.None));
        Assert.Equal(Start, fetch.Store.Read().FirstStepAtUtc);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task AnUnseenHero_GetsAStrongPlayersBestRecentGame()
    {
        FocusTargetedFetch fetch = Fetch();
        await fetch.StepAsync(null, CancellationToken.None);
        now = Start.AddDays(8);
        api.Answer("patches", FakeHeroesProfileApi.Ok(Patches(34)));
        api.Answer(
            "leaderboard",
            FakeHeroesProfileApi.Ok(Board(("Star#1", 2, 12, 75))),
            FakeHeroesProfileApi.Ok(Board(("Star#1", 2, 10, 60), ("Weak#2", 1, 40, 40)))
        );
        api.Answer(
            "players/matches",
            FakeHeroesProfileApi.Ok(
                Matches(
                    (201, "2026-10-08 10:00:00", 1, 3, 2, 4),
                    (202, "2026-10-07 10:00:00", 1, 9, 1, 8)
                )
            )
        );

        TargetedStep step = await fetch.StepAsync(
            new[] { Card(900, "Wiza"), Card(901, "Uthe") },
            CancellationToken.None
        );

        Assert.Equal(TargetedStep.Fetched, step);
        Assert.Equal(new[] { 202 }, downloads);
        Assert.Equal(6, api.Calls.Count(call => call.Path == "leaderboard"));
        (string Path, Dictionary<string, string> Query, DateTimeOffset At) matches =
            api.Calls.Single(call => call.Path == "players/matches");
        Assert.Equal("Star#1", matches.Query["battletag"]);
        Assert.Equal("2", matches.Query["region"]);
        Assert.Equal("Murky", matches.Query["hero"]);
        Assert.Equal("2026-10-03", matches.Query["start_date"]);
        Assert.Contains(
            api.Calls,
            call => call.Path == "leaderboard" && call.Query.GetValueOrDefault("season") == "29"
        );
        FocusTargetedState state = fetch.Store.Read();
        Assert.Equal("Murk", state.Fetched.Single().AttributeId);
        Assert.Equal(now, state.Tried["Murk"]);
    }

    [Fact]
    public async Task AGameSpectateCannotPlay_IsSkipped_ForTheNextOne()
    {
        FocusTargetedFetch fetch = Ready();
        answer = id => id == 202 ? TargetedDownload.NotLaunchable : TargetedDownload.Downloaded;

        Assert.Equal(TargetedStep.Fetched, await fetch.StepAsync(null, CancellationToken.None));
        Assert.Equal(new[] { 202, 201 }, downloads);
    }

    [Fact]
    public async Task TheStep_KeepsToItsInterval_AndItsWaitingCap()
    {
        FocusTargetedFetch fetch = Ready();
        await fetch.StepAsync(null, CancellationToken.None);

        now = now.AddMinutes(30);
        Assert.Equal(TargetedStep.NotDue, await fetch.StepAsync(null, CancellationToken.None));

        now = now.AddHours(2);
        settings.Targeted.MaxWaiting = 1;
        Assert.Equal(
            TargetedStep.EnoughWaiting,
            await fetch.StepAsync(new[] { Card(202, "Wiza") }, CancellationToken.None)
        );
    }

    [Fact]
    public async Task NoStrongPlayer_MovesOnToTheNextHero_AfterRetryAfter()
    {
        FocusTargetedFetch fetch = Ready();
        api.Answer("leaderboard", FakeHeroesProfileApi.Ok(Board(("Weak#2", 1, 40, 40))));

        Assert.Equal(
            TargetedStep.NoStrongPlayer,
            await fetch.StepAsync(null, CancellationToken.None)
        );
        now = now.AddHours(3);
        await fetch.StepAsync(null, CancellationToken.None);

        Assert.Equal(
            new[] { "Li-Ming", "Murky" },
            api.Calls.Where(call => call.Path == "leaderboard")
                .Select(call => call.Query["hero"])
                .Distinct()
        );
    }

    [Fact]
    public async Task AFocusedOrSeenHero_IsNoTarget()
    {
        FocusTargetedFetch fetch = Ready();
        new FocusLedgerStore(FocusLedgerStore.PathFor(root)).Update(ledger =>
        {
            ledger.Put(FocusFixtures.Entry("Murk", "Melee Assassin", now.AddDays(-2)));
            return true;
        });

        await fetch.StepAsync(new[] { Card(900, "Uthe") }, CancellationToken.None);

        Assert.Equal(
            new[] { "Li-Ming" },
            api.Calls.Where(call => call.Path == "leaderboard")
                .Select(call => call.Query["hero"])
                .Distinct()
        );
    }

    [Fact]
    public async Task TheWeeklyBudget_StopsTheFetch()
    {
        FocusTargetedFetch fetch = Ready();
        settings.Targeted.WeeklyCallBudget = 3;

        Assert.Equal(TargetedStep.NoGame, await fetch.StepAsync(null, CancellationToken.None));
        Assert.Equal(3, api.Calls.Count);
        now = now.AddHours(3);
        Assert.Equal(TargetedStep.OverBudget, await fetch.StepAsync(null, CancellationToken.None));
        Assert.Equal(3, api.Calls.Count);
    }

    [Fact]
    public async Task Off_DoesNothing()
    {
        settings.Targeted.Enabled = false;

        Assert.Equal(TargetedStep.Off, await Fetch().StepAsync(null, CancellationToken.None));
        Assert.False(File.Exists(FocusTargetedStore.PathFor(root)));
    }

    /// <summary>A fetch a week after its first step, with a strong player and two games.</summary>
    private FocusTargetedFetch Ready()
    {
        FocusTargetedFetch fetch = Fetch();
        new FocusTargetedStore(FocusTargetedStore.PathFor(root)).Write(
            new FocusTargetedState { FirstStepAtUtc = Start }
        );
        now = Start.AddDays(8);
        api.Answer("patches", FakeHeroesProfileApi.Ok(Patches(34)));
        api.Answer("leaderboard", FakeHeroesProfileApi.Ok(Board(("Star#1", 2, 30, 70))));
        api.Answer(
            "players/matches",
            FakeHeroesProfileApi.Ok(
                Matches(
                    (201, "2026-10-08 10:00:00", 1, 3, 2, 4),
                    (202, "2026-10-07 10:00:00", 1, 9, 1, 8)
                )
            )
        );
        return fetch;
    }

    private FocusTargetedFetch Fetch() =>
        new(
            new HeroesProfileCaller(api, api.Pacer(0), NullLogger.Instance, api.Clock, api.Delay),
            new FocusTargetedStore(FocusTargetedStore.PathFor(root)),
            new FocusLedgerStore(FocusLedgerStore.PathFor(root)),
            settings,
            () => Roster,
            (id, _) =>
            {
                downloads.Add(id);
                return Task.FromResult(answer(id));
            },
            TimeSpan.FromMinutes(5),
            NullLogger<FocusTargetedFetch>.Instance,
            () => now
        );

    private static FocusCard Card(int replayId, string attributeId)
    {
        FocusCard card = FocusFixtures.Card(
            replayId,
            FocusFixtures.Player(0, "Hero", attributeId, "Healer")
        );
        foreach (FocusCardPlayer other in card.Players.Skip(1))
        {
            other.IsAi = true;
        }

        return card;
    }

    private static string Patches(int season) =>
        "{\"patches\":[{\"season\":"
        + season
        + ",\"game_version\":\"2.57.0.98348\"},{\"season\":"
        + (season - 1)
        + "}]}";

    private static string Board(
        params (string Tag, int Region, int Games, double WinRate)[] rows
    ) =>
        "["
        + string.Join(
            ",",
            rows.Select(row =>
                $"{{\"battletag\":\"{row.Tag}\",\"region_id\":{row.Region},\"games_played\":{row.Games},\"win_rate\":{row.WinRate.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"
            )
        )
        + "]";

    private static string Matches(
        params (int Id, string Date, int Won, int Kills, int Deaths, int Assists)[] rows
    ) =>
        "{\"current_page\":1,\"data\":["
        + string.Join(
            ",",
            rows.Select(row =>
                $"{{\"replayID\":{row.Id},\"game_date\":\"{row.Date}\",\"winner\":{row.Won},\"kills\":{row.Kills},\"deaths\":{row.Deaths},\"assists\":{row.Assists}}}"
            )
        )
        + "]}";
}
