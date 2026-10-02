using System;
using System.Collections.Generic;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Twitch;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class StatusPredictionWatcherTests
{
    [Fact]
    public void DecideObserved_VerifiedCompletion_ResolvesOnceWithTheWinner()
    {
        var settled = new HashSet<string>(StringComparer.Ordinal);
        PredictionSignal observed = Observed(PredictionSignalKind.Resolve, winner: 1);
        SpectatorStatus status = Finished("VerifiedCompleted", winner: 1);

        PredictionSignal first = StatusPredictionWatcher.DecideObserved(observed, status, settled);
        PredictionSignal second = StatusPredictionWatcher.DecideObserved(observed, status, settled);

        Assert.Equal(PredictionSignalKind.Resolve, first.Kind);
        Assert.Equal(1, first.WinnerTeam);
        Assert.Equal(10, first.ReplayId);
        Assert.Equal(PredictionSignalKind.None, second.Kind);
    }

    [Fact]
    public void DecideObserved_Crash_CancelsOnceAndPublishesNoWinner()
    {
        var settled = new HashSet<string>(StringComparer.Ordinal);
        PredictionSignal observed = Observed(PredictionSignalKind.Resolve, winner: 0);
        SpectatorStatus status = Finished("ClientCrashed", winner: 0);
        status.Timer = "00:10:00";

        PredictionSignal decided = StatusPredictionWatcher.DecideObserved(
            observed,
            status,
            settled
        );

        Assert.Equal(PredictionSignalKind.Cancel, decided.Kind);
        Assert.Null(decided.WinnerTeam);
        Assert.Equal(
            PredictionSignalKind.None,
            StatusPredictionWatcher.DecideObserved(observed, status, settled).Kind
        );
    }

    [Fact]
    public void DecideObserved_Stop_CancelsOnceAndPublishesNoWinner()
    {
        PredictionSignal decided = StatusPredictionWatcher.DecideObserved(
            Observed(PredictionSignalKind.Resolve, winner: 1),
            Finished("Stopped", winner: 1),
            new HashSet<string>(StringComparer.Ordinal)
        );

        Assert.Equal(PredictionSignalKind.Cancel, decided.Kind);
        Assert.Null(decided.WinnerTeam);
    }

    [Fact]
    public void DecideObserved_Cancel_CancelsOnceAndPublishesNoWinner()
    {
        PredictionSignal decided = StatusPredictionWatcher.DecideObserved(
            Observed(PredictionSignalKind.Cancel, winner: 1),
            Finished("Canceled", winner: 1),
            new HashSet<string>(StringComparer.Ordinal)
        );

        Assert.Equal(PredictionSignalKind.Cancel, decided.Kind);
        Assert.Null(decided.WinnerTeam);
    }

    [Fact]
    public void ApplyDecision_LeavesAnOpenSignalUntouched()
    {
        PredictionSignal open = Observed(PredictionSignalKind.Open, winner: null);

        PredictionSignal decided = StatusPredictionWatcher.ApplyDecision(
            open,
            "VerifiedCompleted",
            matchClockSeen: true,
            alreadySettled: false
        );

        Assert.Equal(PredictionSignalKind.Open, decided.Kind);
        Assert.Null(decided.WinnerTeam);
    }

    private static PredictionSignal Observed(PredictionSignalKind kind, int? winner) =>
        new(kind, 10, "Cursed Hollow", winner, 1);

    private static SpectatorStatus Finished(string outcome, int? winner) =>
        new()
        {
            ReplayId = 10,
            Outcome = outcome,
            CompletedReplayId = 10,
            CompletedWinnerTeam = winner,
            CompletedAt = DateTimeOffset.UnixEpoch,
        };
}
