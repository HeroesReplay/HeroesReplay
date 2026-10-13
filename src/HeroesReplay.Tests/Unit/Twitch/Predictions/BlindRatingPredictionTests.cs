using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Spectating.Session;
using HeroesReplay.Core.Status;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.Twitch.Predictions;
using Microsoft.Extensions.Logging.Abstractions;
using TwitchLib.Api.Helix.Models.Predictions;
using TwitchLib.Client.Interfaces;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch.Predictions;

/// <summary>
/// #411: a Blind Rating replay gets "Guess the rating of the match" (Bronze to Master) instead
/// of Blue/Red, and it resolves to the match's rating after the match.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class BlindRatingPredictionTests : IDisposable
{
    private const int ReplayId = 65840952;
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 21, 0, 0, TimeSpan.Zero);

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "hr-blind-prediction-" + Guid.NewGuid().ToString("N")
    );

    public BlindRatingPredictionTests()
    {
        Directory.CreateDirectory(directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("Gold 3", null, "Gold")]
    [InlineData("Platinum", null, "Platinum")]
    [InlineData("Master", null, "Master")]
    [InlineData("Grandmaster", null, "Master")]
    [InlineData(null, 2, "Silver")]
    [InlineData(null, 7, "Master")]
    [InlineData(null, null, null)]
    public void OutcomeFor_IsTheRankTheGameSceneWouldShow(string rank, int? tier, string expected)
    {
        var replay = new LoadedReplay
        {
            HeroesProfileReplay = new HeroesProfileReplay { Rank = rank, LeagueTier = tier },
        };

        Assert.Equal(expected, RatingPrediction.OutcomeFor(replay));
    }

    [Fact]
    public void OutcomeFor_FallsBackToTheCacheFileName()
    {
        var replay = new LoadedReplay
        {
            FileInfo = new FileInfo(
                Path.Combine(
                    directory,
                    "65840952_Storm League_Diamond 2_Garden of Terror_05d34685_.StormReplay"
                )
            ),
        };

        Assert.Equal("Diamond", RatingPrediction.OutcomeFor(replay));
    }

    [Fact]
    public void Applies_OnlyToABlindReplayWithAKnownRank()
    {
        Assert.True(RatingPrediction.Applies(Blind("Diamond 2")));
        Assert.False(RatingPrediction.Applies(Blind(rank: null)));
        Assert.False(
            RatingPrediction.Applies(
                new LoadedReplay
                {
                    HeroesProfileReplay = new HeroesProfileReplay { Rank = "Diamond 2" },
                }
            )
        );
    }

    /// <summary>Twitch: a title of at most 45 characters, 2 to 10 outcomes of at most 25.</summary>
    [Fact]
    public void Prediction_FitsTwitchLimits()
    {
        Assert.True(RatingPrediction.Title.Length <= MatchPrediction.MaxTitleLength);
        Assert.InRange(RatingPrediction.Outcomes.Count, 2, 10);
        Assert.All(RatingPrediction.Outcomes, outcome => Assert.InRange(outcome.Length, 1, 25));
        Assert.Equal(
            Enum.GetNames<HeroesReplay.Core.Shared.GameRank>(),
            RatingPrediction.Outcomes.ToArray()
        );
        Assert.Equal(new[] { "Blue", "Red" }, RatingPrediction.OutcomesFor(PredictionKind.Winner));
        Assert.Equal(4, RatingPrediction.OutcomeIndex("diamond"));
        Assert.Null(RatingPrediction.OutcomeIndex("Grandmaster"));
        Assert.Equal("Master", RatingPrediction.OutcomeTitle(PredictionKind.Rating, 5));
        Assert.Equal("Red", RatingPrediction.OutcomeTitle(PredictionKind.Winner, 1));
        Assert.Null(RatingPrediction.OutcomeTitle(PredictionKind.Winner, 2));
    }

    [Fact]
    public void ShowLoading_WritesRatingPredictionForABlindReplayWithARank()
    {
        var blind = new SpectatorStatus();
        var unrated = new SpectatorStatus { RatingPrediction = true };
        var ordinary = new SpectatorStatus { RatingPrediction = true };

        GameManager.ShowLoading(blind, Blind("Diamond 2"), current: null);
        GameManager.ShowLoading(unrated, Blind(rank: null), current: null);
        GameManager.ShowLoading(ordinary, new LoadedReplay { ReplayId = ReplayId }, null);

        Assert.True(blind.RatingPrediction);
        Assert.False(blind.SuppressPredictions);
        Assert.False(unrated.RatingPrediction);
        Assert.False(ordinary.RatingPrediction);
    }

    [Fact]
    public void MatchCompletion_WritesTheRatingOnlyForAVerifiedMatch()
    {
        var verified = new SpectatorStatus();
        var stopped = new SpectatorStatus();

        new MatchCompletion().Apply(
            verified,
            MatchOutcome.VerifiedCompleted,
            ReplayId,
            winner: 1,
            T0,
            rating: "Diamond"
        );
        new MatchCompletion().Apply(
            stopped,
            MatchOutcome.Stopped,
            ReplayId,
            winner: null,
            T0,
            rating: "Diamond"
        );

        Assert.Equal("Diamond", verified.CompletedRating);
        Assert.Equal(1, verified.CompletedWinnerTeam);
        Assert.Null(stopped.CompletedRating);
    }

    [Fact]
    public void Tracker_RatingSession_ResolvesToTheRatingNotTheTeam()
    {
        var tracker = new PredictionSessionTracker();
        SpectatorStatus live = Live(rating: true);

        PredictionSignal open = tracker.Observe(live, T0);
        SpectatorStatus ended = Live(rating: false);
        ended.ReplayId = ReplayId + 1;
        ended.UpdatedAt = T0.AddMinutes(20);
        ended.CompletedAt = T0.AddMinutes(19);
        ended.CompletedReplayId = ReplayId;
        ended.CompletedWinnerTeam = 1;
        ended.CompletedRating = "Diamond";
        PredictionSignal resolve = tracker.Observe(ended, T0.AddMinutes(20));

        Assert.Equal(PredictionSignalKind.Open, open.Kind);
        Assert.Equal(PredictionKind.Rating, open.Prediction);
        Assert.Equal(PredictionSignalKind.Resolve, resolve.Kind);
        Assert.Equal(PredictionKind.Rating, resolve.Prediction);
        Assert.Equal(4, resolve.Winner);
    }

    [Fact]
    public void Tracker_RatingSessionWithoutARating_Cancels()
    {
        var tracker = new PredictionSessionTracker();
        tracker.Observe(Live(rating: true), T0);
        SpectatorStatus ended = Live(rating: false);
        ended.UpdatedAt = T0.AddMinutes(20);
        ended.CompletedAt = T0.AddMinutes(19);
        ended.CompletedReplayId = ReplayId;
        ended.CompletedWinnerTeam = 0;
        ended.CompletedRating = null;

        PredictionSignal signal = tracker.Observe(ended, T0.AddMinutes(20));

        Assert.Equal(PredictionSignalKind.Cancel, signal.Kind);
        Assert.Null(signal.Winner);
    }

    [Fact]
    public void Tracker_RestoredRatingSession_StillResolvesToTheRating()
    {
        var tracker = new PredictionSessionTracker();
        tracker.Restore(ReplayId, 1, T0, "Cursed Hollow", PredictionKind.Rating);
        SpectatorStatus ended = Live(rating: false);
        ended.UpdatedAt = T0.AddMinutes(20);
        ended.CompletedAt = T0.AddMinutes(19);
        ended.CompletedReplayId = ReplayId;
        ended.CompletedRating = "Bronze";

        PredictionSignal signal = tracker.Observe(ended, T0.AddMinutes(20));

        Assert.Equal(PredictionSignalKind.Resolve, signal.Kind);
        Assert.Equal(0, signal.Winner);
        Assert.Equal(1, signal.Attempt);
    }

    [Fact]
    public async Task Watcher_BlindReplay_OpensTheRatingPrediction()
    {
        Recorder predictions = await RunToTimer(Blind("Diamond 2"));

        (int replayId, PredictionKind kind) = Assert.Single(predictions.Opened);
        Assert.Equal(ReplayId, replayId);
        Assert.Equal(PredictionKind.Rating, kind);
    }

    [Fact]
    public async Task Watcher_BlindReplayWithoutARank_OpensBlueRed()
    {
        Recorder predictions = await RunToTimer(Blind(rank: null));

        (_, PredictionKind kind) = Assert.Single(predictions.Opened);
        Assert.Equal(PredictionKind.Winner, kind);
    }

    [Fact]
    public void Ledger_EntryFromAnOlderBuild_IsBlueRed()
    {
        string path = Path.Combine(directory, PredictionLedger.FileName);
        File.WriteAllText(
            path,
            """
            {"entries":[{"sessionKey":"10:1","replayId":10,"attempt":1,"predictionId":"p",
            "blueOutcomeId":"blue","redOutcomeId":"red","state":"Open","intent":"None"}]}
            """
        );

        PredictionLedgerEntry entry = PredictionLedger.Load(path).FindUnsettled(10);

        Assert.Equal(PredictionKind.Winner, entry.Kind);
        Assert.Equal("blue", entry.OutcomeIdFor(0));
        Assert.Equal("red", entry.OutcomeIdFor(1));
        Assert.Null(entry.OutcomeIdFor(null));
    }

    [Fact]
    public void Report_RatingAddsEveryWrongGuessAndNamesTheMostBacked()
    {
        Prediction prediction = RatingPrediction_(winning: "Diamond");

        PredictionReport report = PredictionReportBuilder.FromPrediction(
            prediction,
            streaks: null,
            "Cursed Hollow",
            new Random(1),
            PredictionKind.Rating
        );

        Assert.Equal(PredictionKind.Rating, report.Kind);
        Assert.Equal("Diamond", report.WinningOutcome);
        Assert.Equal("Gold", report.LosingOutcome);
        Assert.Equal(2, report.WinnerVoters);
        Assert.Equal(9, report.LoserVoters);
        Assert.Equal(2700, report.LoserPoints);
        Assert.Equal(PredictionVerdictKind.Upset, PredictionVerdict.Kind(report));
        Assert.DoesNotContain(" wins", report.Verdict, StringComparison.Ordinal);
        Assert.Contains("Other guesses", PredictionReportPage.ToHtml(report));
    }

    [Fact]
    public void Verdict_RatingOpeningsNeverUseTheTeamLines()
    {
        foreach (PredictionVerdictKind kind in Enum.GetValues<PredictionVerdictKind>())
        {
            List<string> lines = PredictionVerdict
                .OpeningsFor(kind, PredictionKind.Rating)
                .ToList();
            Assert.NotEmpty(lines);
            Assert.DoesNotContain(lines, line => PredictionVerdict.Contest.Contains(line));
            Assert.All(lines, line => Assert.Contains("{winner}", line));
        }
    }

    [Fact]
    public void Verdict_RatingCalloutNeverSaysTheVictimBackedTheMostPopularGuess()
    {
        Prediction prediction = RatingPrediction_(winning: "Diamond");
        for (int seed = 0; seed < 40; seed++)
        {
            PredictionReport report = PredictionReportBuilder.FromPrediction(
                prediction,
                streaks: null,
                "Cursed Hollow",
                new Random(seed),
                PredictionKind.Rating
            );

            Assert.DoesNotContain("points on Gold", report.Verdict, StringComparison.Ordinal);
        }
    }

    private async Task<Recorder> RunToTimer(LoadedReplay loaded)
    {
        var store = new SpectatorStatusStore(Path.Combine(directory, "status.json"));
        store.Patch(status => GameManager.ShowLoading(status, loaded, current: null));
        store.Patch(status =>
        {
            status.Phase = "TimerDetected";
            status.Timer = "00:00:05";
            status.SnapshotStale = false;
        });

        var predictions = new Recorder();
        var watcher = new StatusPredictionWatcher(
            NullLogger<StatusPredictionWatcher>.Instance,
            new AppSettings { Twitch = new TwitchSettings { EnableChatBot = false } },
            store,
            predictions,
            SilentTwitch.Create()
        )
        {
            ReplaySessionFilePath = Path.Combine(directory, "replay-sessions.txt"),
        };

        await watcher.StepAsync(new PredictionSessionTracker(), CancellationToken.None);
        return predictions;
    }

    private static SpectatorStatus Live(bool rating) =>
        new()
        {
            UpdatedAt = T0,
            SpectatorRunning = true,
            Phase = "TimerDetected",
            Map = "Cursed Hollow",
            ReplayId = ReplayId,
            RatingPrediction = rating,
        };

    private static LoadedReplay Blind(string rank) =>
        new()
        {
            ReplayId = ReplayId,
            HeroesProfileReplay = new HeroesProfileReplay { Map = "Cursed Hollow", Rank = rank },
            RewardQueueItem = new RewardQueueItem
            {
                Request = new RewardRequest
                {
                    Login = "viewer",
                    RewardTitle = "Blind Rating",
                    Blind = true,
                    RedemptionId = Guid.NewGuid(),
                },
            },
        };

    /// <summary>
    /// Diamond won with 2 viewers. Gold drew the most of the five wrong guesses (5 viewers, 2000
    /// points); Ana's top prediction on Gold cost 1500 points.
    /// </summary>
    private static Prediction RatingPrediction_(string winning)
    {
        int[] voters = { 1, 1, 5, 1, 2, 1 };
        int[] points = { 100, 200, 2000, 300, 900, 100 };
        var outcomes = new List<string>();
        for (int index = 0; index < RatingPrediction.Outcomes.Count; index++)
        {
            string title = RatingPrediction.Outcomes[index];
            string predictors =
                title == "Gold"
                    ? "{\"user_id\":\"1\",\"user_name\":\"Ana\",\"user_login\":\"ana\",\"channel_points_used\":1500,\"channel_points_won\":0}"
                : title == winning
                    ? "{\"user_id\":\"2\",\"user_name\":\"Bo\",\"user_login\":\"bo\",\"channel_points_used\":500,\"channel_points_won\":3000}"
                : "";
            outcomes.Add(
                "{\"id\":\"rating-"
                    + title.ToLowerInvariant()
                    + "\",\"title\":\""
                    + title
                    + "\",\"users\":"
                    + voters[index]
                    + ",\"channel_points\":"
                    + points[index]
                    + ",\"top_predictors\":["
                    + predictors
                    + "]}"
            );
        }

        return Newtonsoft.Json.JsonConvert.DeserializeObject<Prediction>(
            "{\"id\":\"rating\",\"title\":\""
                + RatingPrediction.Title
                + "\",\"status\":\"RESOLVED\",\"winning_outcome_id\":\"rating-"
                + winning.ToLowerInvariant()
                + "\",\"outcomes\":["
                + string.Join(",", outcomes)
                + "]}"
        );
    }

    private sealed class Recorder : IMatchPredictionService
    {
        public List<(int ReplayId, PredictionKind Kind)> Opened { get; } = new();

        public Task<bool> OpenAsync(
            int replayId,
            string map,
            PredictionKind kind,
            CancellationToken cancellationToken
        )
        {
            Opened.Add((replayId, kind));
            return Task.FromResult(true);
        }

        public Task RetryPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(LoadedReplay replay, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ResolveAsync(LoadedReplay replay, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ResolveTeamAsync(int? winningTeam, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ResolveReplayAsync(
            int replayId,
            int? winningTeam,
            CancellationToken cancellationToken
        ) => Task.CompletedTask;

        public Task<PredictionReconcileResult> ReconcileAsync(
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task TestAsync(int? winningTeam, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private class SilentTwitch : DispatchProxy
    {
        public static ITwitchClient Create() => DispatchProxy.Create<ITwitchClient, SilentTwitch>();

        protected override object Invoke(MethodInfo targetMethod, object[] args) =>
            targetMethod.ReturnType == typeof(Task) ? Task.CompletedTask : null;
    }
}
