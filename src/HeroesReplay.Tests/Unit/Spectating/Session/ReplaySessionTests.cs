using System;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Spectating.Session;
using HeroesReplay.Core.Status;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Session;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplaySessionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 29, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ReplaySessionKind.Played, false, true)]
    [InlineData(ReplaySessionKind.AwardFinished, false, true)]
    [InlineData(ReplaySessionKind.Unplayed, true, true)]
    [InlineData(ReplaySessionKind.Unplayed, false, false)]
    [InlineData(ReplaySessionKind.Held, false, false)]
    public void MadeMatchProgress_IsTheClockOrTheAwardScreen(
        ReplaySessionKind kind,
        bool clockSeen,
        bool expected
    )
    {
        Assert.Equal(expected, ReplaySession.MadeMatchProgress(kind, clockSeen));
    }

    [Fact]
    public void Classify_VerifiedCompletionConsumesTheReplay()
    {
        Assert.Equal(
            ReplaySessionKind.Played,
            ReplaySession.Classify(MatchOutcome.VerifiedCompleted)
        );
        Assert.False(ReplaySession.StaysQueued(ReplaySessionKind.Played));
    }

    [Theory]
    [InlineData(MatchOutcome.ClientCrashed)]
    [InlineData(MatchOutcome.ClientHung)]
    [InlineData(MatchOutcome.LoadTimedOut)]
    [InlineData(MatchOutcome.Stopped)]
    [InlineData(MatchOutcome.Canceled)]
    [InlineData(MatchOutcome.None)]
    public void Classify_InterruptedOutcomeStaysQueuedWithoutAHold(MatchOutcome outcome)
    {
        Assert.Equal(ReplaySessionKind.Unplayed, ReplaySession.Classify(outcome));
        Assert.True(ReplaySession.StaysQueued(ReplaySessionKind.Unplayed));
    }

    [Theory]
    [InlineData(ClientHoldReason.VersionMismatch, MatchOutcome.VersionMismatch)]
    [InlineData(ClientHoldReason.RegionUnavailable, MatchOutcome.RegionUnavailable)]
    [InlineData(ClientHoldReason.BuildNotInstalled, MatchOutcome.BuildNotInstalled)]
    public void FromHold_DialogsStayHeld(ClientHoldReason hold, MatchOutcome expected)
    {
        MatchOutcome outcome = MatchCompletion.FromHold(hold);
        Assert.Equal(expected, outcome);
        Assert.Equal(ReplaySessionKind.Held, ReplaySession.Classify(outcome));
        Assert.True(ReplaySession.StaysQueued(ReplaySessionKind.Held));
    }

    [Fact]
    public void FromHold_NoneIsNotADialog()
    {
        Assert.Equal(MatchOutcome.None, MatchCompletion.FromHold(ClientHoldReason.None));
    }

    [Fact]
    public void FromHold_AwardScreenMovesOnWithoutConsumingTheReplay()
    {
        MatchOutcome outcome = MatchCompletion.FromHold(ClientHoldReason.AwardScreen);

        Assert.Equal(MatchOutcome.AwardScreen, outcome);
        Assert.Equal(ReplaySessionKind.AwardFinished, ReplaySession.Classify(outcome));
        Assert.False(ReplaySession.StaysQueued(ReplaySessionKind.AwardFinished));
    }

    [Fact]
    public void FromHold_ClientNotReadyStaysQueuedWithoutADialogHold()
    {
        MatchOutcome outcome = MatchCompletion.FromHold(ClientHoldReason.ClientNotReady);

        Assert.Equal(MatchOutcome.LoadTimedOut, outcome);
        Assert.Equal(ReplaySessionKind.Unplayed, ReplaySession.Classify(outcome));
        Assert.True(ReplaySession.StaysQueued(ReplaySessionKind.Unplayed));
    }

    [Fact]
    public void StaysQueued_KeepsHeldAndUnplayed()
    {
        Assert.True(ReplaySession.StaysQueued(ReplaySessionKind.Held));
        Assert.True(ReplaySession.StaysQueued(ReplaySessionKind.Unplayed));
        Assert.False(ReplaySession.StaysQueued(ReplaySessionKind.Played));
    }

    [Fact]
    public void Normalize_VerifiedRequiresTheMatchClock()
    {
        Assert.Equal(
            MatchOutcome.VerifiedCompleted,
            MatchCompletion.Normalize(
                MatchOutcome.VerifiedCompleted,
                matchClockSeen: true,
                consoleStopped: false
            )
        );
        Assert.Equal(
            MatchOutcome.Canceled,
            MatchCompletion.Normalize(
                MatchOutcome.VerifiedCompleted,
                matchClockSeen: false,
                consoleStopped: false
            )
        );
        Assert.Equal(
            MatchOutcome.Stopped,
            MatchCompletion.Normalize(
                MatchOutcome.VerifiedCompleted,
                matchClockSeen: false,
                consoleStopped: true
            )
        );
    }

    [Fact]
    public void Normalize_ConsoleStopIsStoppedWhenNothingElseEndedTheSession()
    {
        Assert.Equal(
            MatchOutcome.Stopped,
            MatchCompletion.Normalize(MatchOutcome.None, matchClockSeen: true, consoleStopped: true)
        );
        Assert.Equal(
            MatchOutcome.Canceled,
            MatchCompletion.Normalize(
                MatchOutcome.None,
                matchClockSeen: false,
                consoleStopped: false
            )
        );
    }

    [Theory]
    [InlineData(MatchOutcome.ClientCrashed)]
    [InlineData(MatchOutcome.ClientHung)]
    [InlineData(MatchOutcome.LoadTimedOut)]
    [InlineData(MatchOutcome.VersionMismatch)]
    [InlineData(MatchOutcome.RegionUnavailable)]
    [InlineData(MatchOutcome.BuildNotInstalled)]
    public void Normalize_KeepsTheReasonThatEndedTheSession(MatchOutcome outcome)
    {
        Assert.Equal(
            outcome,
            MatchCompletion.Normalize(outcome, matchClockSeen: true, consoleStopped: true)
        );
    }

    [Theory]
    [InlineData(MatchOutcome.ClientCrashed)]
    [InlineData(MatchOutcome.ClientHung)]
    [InlineData(MatchOutcome.VersionMismatch)]
    [InlineData(MatchOutcome.RegionUnavailable)]
    [InlineData(MatchOutcome.BuildNotInstalled)]
    [InlineData(MatchOutcome.LoadTimedOut)]
    [InlineData(MatchOutcome.Stopped)]
    [InlineData(MatchOutcome.Canceled)]
    [InlineData(MatchOutcome.None)]
    public void Interrupted_PublishesNoWinnerAndCannotInventOne(MatchOutcome outcome)
    {
        var status = new SpectatorStatus();
        var completion = new MatchCompletion();
        completion.Apply(status, outcome, 77, 1, T0);
        completion.Apply(status, MatchOutcome.VerifiedCompleted, 77, 0, T0.AddMinutes(5));

        Assert.Null(status.CompletedAt);
        Assert.Null(status.CompletedReplayId);
        Assert.Null(status.CompletedWinnerTeam);
        Assert.True(ReplaySession.StaysQueued(ReplaySession.Classify(outcome)));
    }

    [Fact]
    public void Interrupted_LeavesAnEarlierCompletionUntouched()
    {
        var status = new SpectatorStatus
        {
            CompletedAt = T0,
            CompletedReplayId = 10,
            CompletedWinnerTeam = 1,
        };
        var completion = new MatchCompletion();

        completion.Apply(status, MatchOutcome.ClientCrashed, 11, 0, T0.AddMinutes(30));

        Assert.Equal(T0, status.CompletedAt);
        Assert.Equal(10, status.CompletedReplayId);
        Assert.Equal(1, status.CompletedWinnerTeam);
    }

    [Fact]
    public void Verified_SecondWriteKeepsTheWinnerAndTheFirstTimestamp()
    {
        var status = new SpectatorStatus();
        var completion = new MatchCompletion();
        completion.Apply(status, MatchOutcome.VerifiedCompleted, 42, 0, T0);
        completion.Apply(status, MatchOutcome.VerifiedCompleted, 42, 1, T0.AddMinutes(3));

        Assert.Equal(T0, status.CompletedAt);
        Assert.Equal(42, status.CompletedReplayId);
        Assert.Equal(0, status.CompletedWinnerTeam);
        Assert.False(
            ReplaySession.StaysQueued(ReplaySession.Classify(MatchOutcome.VerifiedCompleted))
        );
    }

    [Fact]
    public void NextSession_CanPublishItsOwnVerifiedCompletion()
    {
        var status = new SpectatorStatus();
        var completion = new MatchCompletion();
        completion.Apply(status, MatchOutcome.VerifiedCompleted, 42, 0, T0);
        completion.Reset();
        DateTimeOffset second = T0.AddMinutes(20);
        completion.Apply(status, MatchOutcome.VerifiedCompleted, 43, 1, second);

        Assert.Equal(second, status.CompletedAt);
        Assert.Equal(43, status.CompletedReplayId);
        Assert.Equal(1, status.CompletedWinnerTeam);
    }

    [Fact]
    public void AllowsMedia_RequiresVerifiedCompletionAndAPublishableRecording()
    {
        Assert.True(
            MatchCompletion.AllowsMedia(
                MatchOutcome.VerifiedCompleted,
                4,
                MatchRecording.MinimumLength
            )
        );
        Assert.False(
            MatchCompletion.AllowsMedia(MatchOutcome.VerifiedCompleted, 4, TimeSpan.FromSeconds(90))
        );
        Assert.False(
            MatchCompletion.AllowsMedia(
                MatchOutcome.VerifiedCompleted,
                0,
                MatchRecording.MinimumLength
            )
        );
        Assert.False(
            MatchCompletion.AllowsMedia(MatchOutcome.ClientCrashed, 4, MatchRecording.MinimumLength)
        );
        Assert.False(
            MatchCompletion.AllowsMedia(MatchOutcome.ClientHung, 4, TimeSpan.FromMinutes(10))
        );
        Assert.False(
            MatchCompletion.AllowsMedia(MatchOutcome.LoadTimedOut, 1, TimeSpan.FromMinutes(3))
        );
    }
}
