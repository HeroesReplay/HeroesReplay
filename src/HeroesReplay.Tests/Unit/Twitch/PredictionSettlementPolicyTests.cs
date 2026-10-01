using System;
using HeroesReplay.Core.Services.Twitch;
using TwitchLib.Api.Core.Exceptions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PredictionSettlementPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Plan_PendingResolveWhenBackoffElapsed_Resolves()
    {
        PredictionSettlementPlan plan = PredictionSettlementPolicy.Plan(
            PredictionLedgerState.Pending,
            PredictionIntent.Resolve,
            PredictionRemoteState.Locked,
            SettlementFailure.None,
            backoffElapsed: true
        );

        Assert.True(plan.ShouldResolve);
        Assert.False(plan.ShouldCancel);
        Assert.False(plan.ShouldRetry);
        Assert.False(plan.ClearIntent);
    }

    [Fact]
    public void Plan_PendingCancelWhenActive_Cancels()
    {
        PredictionSettlementPlan plan = PredictionSettlementPolicy.Plan(
            PredictionLedgerState.Pending,
            PredictionIntent.Cancel,
            PredictionRemoteState.Active,
            SettlementFailure.None,
            backoffElapsed: true
        );

        Assert.True(plan.ShouldCancel);
        Assert.False(plan.ShouldResolve);
        Assert.False(plan.ShouldRetry);
    }

    [Fact]
    public void Plan_CancelLockedPrediction_CancelsRatherThanResolves()
    {
        PredictionSettlementPlan plan = PredictionSettlementPolicy.Plan(
            PredictionLedgerState.Pending,
            PredictionIntent.Cancel,
            PredictionRemoteState.Locked,
            SettlementFailure.None,
            backoffElapsed: true
        );

        Assert.True(plan.ShouldCancel);
        Assert.False(plan.ShouldResolve);
        Assert.False(plan.ClearIntent);
    }

    [Fact]
    public void Plan_BeforeBackoff_DoesNotCall()
    {
        PredictionSettlementPlan plan = PredictionSettlementPolicy.Plan(
            PredictionLedgerState.Pending,
            PredictionIntent.Resolve,
            PredictionRemoteState.Active,
            SettlementFailure.None,
            backoffElapsed: false
        );

        Assert.True(plan.ShouldRetry);
        Assert.False(plan.ShouldResolve);
        Assert.False(plan.ShouldCancel);
        Assert.False(plan.ClearIntent);
    }

    [Fact]
    public void Plan_TransientFailure_RetriesWithoutClearing()
    {
        PredictionSettlementPlan plan = PredictionSettlementPolicy.Plan(
            PredictionLedgerState.Pending,
            PredictionIntent.Resolve,
            PredictionRemoteState.Locked,
            SettlementFailure.Transient,
            backoffElapsed: true
        );

        Assert.True(plan.ShouldRetry);
        Assert.False(plan.ShouldResolve);
        Assert.False(plan.ShouldCancel);
        Assert.False(plan.ClearIntent);
        Assert.False(plan.Terminal);
    }

    [Fact]
    public void Plan_RemoteResolved_ClearsIntent()
    {
        PredictionSettlementPlan plan = PredictionSettlementPolicy.Plan(
            PredictionLedgerState.Pending,
            PredictionIntent.Resolve,
            PredictionRemoteState.Resolved,
            SettlementFailure.None,
            backoffElapsed: true
        );

        Assert.True(plan.ClearIntent);
        Assert.False(plan.Terminal);
        Assert.False(plan.ShouldRetry);
        Assert.False(plan.ShouldResolve);
        Assert.False(plan.CallsTwitch);
    }

    [Fact]
    public void Plan_Settled_IsNoOp()
    {
        PredictionSettlementPlan plan = PredictionSettlementPolicy.Plan(
            PredictionLedgerState.Settled,
            PredictionIntent.None,
            PredictionRemoteState.Active,
            SettlementFailure.None,
            backoffElapsed: true
        );

        Assert.False(plan.ShouldResolve);
        Assert.False(plan.ShouldCancel);
        Assert.False(plan.ShouldRetry);
        Assert.False(plan.ClearIntent);
        Assert.False(plan.Terminal);
    }

    [Fact]
    public void Plan_TerminalFailure_ClearsIntentAndStops()
    {
        PredictionSettlementPlan plan = PredictionSettlementPolicy.Plan(
            PredictionLedgerState.Pending,
            PredictionIntent.Cancel,
            PredictionRemoteState.Locked,
            SettlementFailure.Terminal,
            backoffElapsed: true
        );

        Assert.True(plan.Terminal);
        Assert.True(plan.ClearIntent);
        Assert.False(plan.ShouldRetry);
        Assert.False(plan.ShouldCancel);
        Assert.False(plan.ShouldResolve);
    }

    [Fact]
    public void Backoff_WaitsUntilTheRecordedInstant()
    {
        DateTimeOffset due = T0.AddSeconds(2);

        Assert.False(PredictionSettlementPolicy.BackoffElapsed(due.AddSeconds(-1), due));
        Assert.True(PredictionSettlementPolicy.BackoffElapsed(due, due));
        Assert.Equal(T0.AddSeconds(2), PredictionSettlementPolicy.NextRetryAt(T0, 1));
        Assert.Equal(T0.AddSeconds(60), PredictionSettlementPolicy.NextRetryAt(T0, 8));
    }

    [Fact]
    public void Classify_TokenAndServerErrors()
    {
        Assert.Equal(
            SettlementFailure.Transient,
            PredictionSettlementPolicy.Classify(new InternalServerErrorException("down"))
        );
        Assert.Equal(
            SettlementFailure.Terminal,
            PredictionSettlementPolicy.Classify(new BadTokenException("rejected"))
        );
        Assert.Equal(SettlementFailure.None, PredictionSettlementPolicy.Classify(null));
    }

    [Fact]
    public void Reconcile_MatchingLockedId_ResumesThatReplay()
    {
        PredictionReconcileResult plan = PredictionSettlementPolicy.Reconcile(
            new[] { Entry(10, 1, "owned", "Cursed Hollow: who wins?", PredictionLedgerState.Open) },
            new[]
            {
                new PredictionRemoteRef(
                    "owned",
                    "Different title: who wins?",
                    PredictionRemoteState.Locked
                ),
            },
            remoteFetched: true
        );

        Assert.NotNull(plan.Resume);
        Assert.Equal("owned", plan.Resume.PredictionId);
        Assert.Equal(10, plan.Resume.ReplayId);
        Assert.Equal(1, plan.Resume.Attempt);
        Assert.Empty(plan.ForeignPredictionIds);
        Assert.Empty(plan.OrphanedPredictionIds);
    }

    [Fact]
    public void Reconcile_SameTitleDifferentId_IsForeignAndOrphaned()
    {
        PredictionReconcileResult plan = PredictionSettlementPolicy.Reconcile(
            new[] { Entry(10, 1, "owned", "Cursed Hollow: who wins?", PredictionLedgerState.Open) },
            new[]
            {
                new PredictionRemoteRef(
                    "other",
                    "Cursed Hollow: who wins?",
                    PredictionRemoteState.Locked
                ),
            },
            remoteFetched: true
        );

        Assert.Null(plan.Resume);
        Assert.Equal(new[] { "other" }, plan.ForeignPredictionIds);
        Assert.Equal(new[] { "owned" }, plan.OrphanedPredictionIds);
    }

    [Fact]
    public void Reconcile_PendingEntry_IsNotResumed()
    {
        PredictionReconcileResult plan = PredictionSettlementPolicy.Reconcile(
            new[]
            {
                Entry(10, 1, "owned", "Cursed Hollow: who wins?", PredictionLedgerState.Pending),
            },
            new[]
            {
                new PredictionRemoteRef(
                    "owned",
                    "Cursed Hollow: who wins?",
                    PredictionRemoteState.Active
                ),
            },
            remoteFetched: true
        );

        Assert.Null(plan.Resume);
        Assert.Empty(plan.ForeignPredictionIds);
        Assert.Empty(plan.OrphanedPredictionIds);
    }

    [Fact]
    public void Ledger_SameTitle_UsesDistinctReplayAttempts()
    {
        string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "hr-ledger-key-" + Guid.NewGuid().ToString("N")
        );
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            string path = PredictionLedger.PathFor(directory);
            PredictionLedger ledger = PredictionLedger.Load(path);
            ledger.Upsert(
                Entry(10, 1, "p1", "Cursed Hollow: who wins?", PredictionLedgerState.Open)
            );
            ledger.Upsert(
                Entry(11, 1, "p2", "Cursed Hollow: who wins?", PredictionLedgerState.Open)
            );

            PredictionLedger reloaded = PredictionLedger.Load(path);
            Assert.Equal("10:1", reloaded.FindByPredictionId("p1").SessionKey);
            Assert.Equal("11:1", reloaded.FindByPredictionId("p2").SessionKey);
            Assert.Equal("Cursed Hollow: who wins?", reloaded.FindByPredictionId("p1").Title);
            Assert.NotEqual(
                reloaded.FindByPredictionId("p1").SessionKey,
                reloaded.FindByPredictionId("p1").Title
            );
            Assert.Equal(2, reloaded.NextAttempt(10));
        }
        finally
        {
            try
            {
                System.IO.Directory.Delete(directory, true);
            }
            catch (System.IO.IOException) { }
        }
    }

    private static PredictionLedgerEntry Entry(
        int replayId,
        int attempt,
        string predictionId,
        string title,
        PredictionLedgerState state
    ) =>
        new PredictionLedgerEntry
        {
            SessionKey = replayId + ":" + attempt,
            ReplayId = replayId,
            Attempt = attempt,
            PredictionId = predictionId,
            Title = title,
            Map = "Cursed Hollow",
            CreatedAt = T0.AddMinutes(attempt),
            State = state,
            Intent =
                state == PredictionLedgerState.Pending
                    ? PredictionIntent.Resolve
                    : PredictionIntent.None,
        };
}
