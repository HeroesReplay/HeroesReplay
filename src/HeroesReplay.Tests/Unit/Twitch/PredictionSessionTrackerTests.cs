using System;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Twitch;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PredictionSessionTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 21, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Observe_TimerDetected_OpensOnce()
    {
        var tracker = new PredictionSessionTracker();
        SpectatorStatus live = Live(replayId: 10, at: T0);

        PredictionSignal first = tracker.Observe(live, T0);
        PredictionSignal second = tracker.Observe(live, T0.AddSeconds(1));

        Assert.Equal(PredictionSignalKind.Open, first.Kind);
        Assert.Equal(10, first.ReplayId);
        Assert.Equal("Cursed Hollow", first.Map);
        Assert.Equal(PredictionSignalKind.None, second.Kind);
    }

    [Fact]
    public void Observe_CompletionAfterOpen_ResolvesTeamZero()
    {
        var tracker = new PredictionSessionTracker();
        tracker.Observe(Live(replayId: 10, at: T0), T0);

        SpectatorStatus ended = Live(replayId: 11, at: T0.AddMinutes(20));
        ended.CompletedAt = T0.AddMinutes(19);
        ended.CompletedReplayId = 10;
        ended.CompletedWinnerTeam = 0;

        PredictionSignal signal = tracker.Observe(ended, T0.AddMinutes(20));

        Assert.Equal(PredictionSignalKind.Resolve, signal.Kind);
        Assert.Equal(10, signal.ReplayId);
        Assert.Equal(0, signal.WinnerTeam);
    }

    [Fact]
    public void Observe_CompletionWithoutWinner_Cancels()
    {
        var tracker = new PredictionSessionTracker();
        tracker.Observe(Live(replayId: 10, at: T0), T0);

        SpectatorStatus ended = Idle(T0.AddMinutes(5));
        ended.Phase = "EndDetected";
        ended.CompletedAt = T0.AddMinutes(5);
        ended.CompletedReplayId = 10;
        ended.CompletedWinnerTeam = null;

        PredictionSignal signal = tracker.Observe(ended, T0.AddMinutes(5));

        Assert.Equal(PredictionSignalKind.Cancel, signal.Kind);
        Assert.Equal(10, signal.ReplayId);
    }

    [Fact]
    public void Observe_OldCompletion_DoesNotResolveUntilANewerOne()
    {
        var tracker = new PredictionSessionTracker();
        SpectatorStatus replay = Live(replayId: 10, at: T0);
        replay.CompletedAt = T0.AddMinutes(-30);
        replay.CompletedReplayId = 10;
        replay.CompletedWinnerTeam = 1;

        Assert.Equal(PredictionSignalKind.Open, tracker.Observe(replay, T0).Kind);
        Assert.Equal(PredictionSignalKind.None, tracker.Observe(replay, T0.AddSeconds(2)).Kind);

        replay.CompletedAt = T0.AddMinutes(15);
        PredictionSignal signal = tracker.Observe(replay, T0.AddMinutes(15));
        Assert.Equal(PredictionSignalKind.Resolve, signal.Kind);
        Assert.Equal(1, signal.WinnerTeam);
    }

    [Fact]
    public void Observe_NextReplayWithoutCompletion_Cancels()
    {
        var tracker = new PredictionSessionTracker();
        tracker.Observe(Live(replayId: 10, at: T0), T0);

        SpectatorStatus next = Live(replayId: 11, at: T0.AddMinutes(1));
        next.Phase = "Loading";

        PredictionSignal signal = tracker.Observe(next, T0.AddMinutes(1));

        Assert.Equal(PredictionSignalKind.Cancel, signal.Kind);
        Assert.Equal(10, signal.ReplayId);
    }

    [Fact]
    public void Observe_StaleTimer_DoesNotCancelUntilAbandonWindow()
    {
        var tracker = new PredictionSessionTracker();
        tracker.Observe(Live(replayId: 10, at: T0), T0);

        SpectatorStatus stale = Live(replayId: 10, at: T0);
        stale.SnapshotStale = true;
        stale.SpectatorRunning = false;

        Assert.Equal(PredictionSignalKind.None, tracker.Observe(stale, T0.AddSeconds(20)).Kind);

        PredictionSignal abandoned = tracker.Observe(
            stale,
            T0.Add(PredictionSessionTracker.AbandonAfter).AddSeconds(1)
        );
        Assert.Equal(PredictionSignalKind.Cancel, abandoned.Kind);
    }

    [Fact]
    public void Observe_IdleWithoutAnOpenPrediction_DoesNothing()
    {
        var tracker = new PredictionSessionTracker();
        PredictionSignal signal = tracker.Observe(Idle(T0), T0);
        Assert.Equal(PredictionSignalKind.None, signal.Kind);
    }

    [Fact]
    public void Observe_AfterResolve_NextTimerOpens()
    {
        var tracker = new PredictionSessionTracker();
        tracker.Observe(Live(replayId: 10, at: T0), T0);
        SpectatorStatus ended = Idle(T0.AddMinutes(10));
        ended.Phase = "EndDetected";
        ended.CompletedAt = T0.AddMinutes(10);
        ended.CompletedReplayId = 10;
        ended.CompletedWinnerTeam = 1;
        Assert.Equal(PredictionSignalKind.Resolve, tracker.Observe(ended, T0.AddMinutes(10)).Kind);

        PredictionSignal next = tracker.Observe(
            Live(replayId: 12, at: T0.AddMinutes(12)),
            T0.AddMinutes(12)
        );
        Assert.Equal(PredictionSignalKind.Open, next.Kind);
        Assert.Equal(12, next.ReplayId);
    }

    [Fact]
    public void Observe_ViewerEnteredReplayId_DoesNotOpen()
    {
        var tracker = new PredictionSessionTracker();
        SpectatorStatus live = Live(replayId: 10, at: T0);
        live.SuppressPredictions = true;

        PredictionSignal first = tracker.Observe(live, T0);
        PredictionSignal second = tracker.Observe(live, T0.AddSeconds(1));

        Assert.Equal(PredictionSignalKind.Disabled, first.Kind);
        Assert.Equal(PredictionSignalKind.None, second.Kind);
    }

    private static SpectatorStatus Live(int replayId, DateTimeOffset at) =>
        new()
        {
            UpdatedAt = at,
            SpectatorRunning = true,
            Phase = "TimerDetected",
            Map = "Cursed Hollow",
            ReplayId = replayId,
        };

    private static SpectatorStatus Idle(DateTimeOffset at) =>
        new()
        {
            UpdatedAt = at,
            SpectatorRunning = false,
            Phase = "Idle",
        };
}
