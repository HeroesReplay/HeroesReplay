using System;
using System.Collections.Generic;
using TwitchLib.Api.Core.Exceptions;

namespace HeroesReplay.Core.Twitch.Predictions;

public enum PredictionRemoteState
{
    Unknown,
    Active,
    Locked,
    Resolved,
    Canceled,
    Missing,
}

public enum SettlementFailure
{
    None,
    Transient,
    Terminal,
}

public readonly record struct PredictionSettlementPlan(
    bool ShouldResolve,
    bool ShouldCancel,
    bool ShouldRetry,
    bool ClearIntent,
    bool Terminal
)
{
    public static PredictionSettlementPlan None { get; } = new(false, false, false, false, false);

    public bool CallsTwitch => ShouldResolve || ShouldCancel;
}

public readonly record struct PredictionRemoteRef(
    string PredictionId,
    string Title,
    PredictionRemoteState State
);

public sealed class PredictionResume
{
    public int ReplayId { get; init; }
    public int Attempt { get; init; }
    public string PredictionId { get; init; }
    public string Map { get; init; }
    public DateTimeOffset OpenedAt { get; init; }
}

public sealed class PredictionReconcileResult
{
    public PredictionReconcileResult(
        PredictionResume resume,
        IReadOnlyList<string> foreignPredictionIds,
        IReadOnlyList<string> orphanedPredictionIds
    )
    {
        Resume = resume;
        ForeignPredictionIds = foreignPredictionIds ?? Array.Empty<string>();
        OrphanedPredictionIds = orphanedPredictionIds ?? Array.Empty<string>();
    }

    public static PredictionReconcileResult Empty { get; } =
        new PredictionReconcileResult(null, Array.Empty<string>(), Array.Empty<string>());

    public PredictionResume Resume { get; }
    public IReadOnlyList<string> ForeignPredictionIds { get; }
    public IReadOnlyList<string> OrphanedPredictionIds { get; }
}

public static class PredictionSettlementPolicy
{
    public static PredictionSettlementPlan Plan(
        PredictionLedgerState state,
        PredictionIntent intent,
        PredictionRemoteState remote,
        SettlementFailure failure,
        bool backoffElapsed
    )
    {
        if (state is PredictionLedgerState.Settled or PredictionLedgerState.Terminal)
        {
            return PredictionSettlementPlan.None;
        }

        if (remote is PredictionRemoteState.Resolved or PredictionRemoteState.Canceled)
        {
            return new PredictionSettlementPlan(
                ShouldResolve: false,
                ShouldCancel: false,
                ShouldRetry: false,
                ClearIntent: true,
                Terminal: false
            );
        }

        if (failure == SettlementFailure.Terminal)
        {
            return new PredictionSettlementPlan(
                ShouldResolve: false,
                ShouldCancel: false,
                ShouldRetry: false,
                ClearIntent: true,
                Terminal: true
            );
        }

        if (intent == PredictionIntent.None)
        {
            return PredictionSettlementPlan.None;
        }

        if (failure == SettlementFailure.Transient || !backoffElapsed)
        {
            return new PredictionSettlementPlan(
                ShouldResolve: false,
                ShouldCancel: false,
                ShouldRetry: true,
                ClearIntent: false,
                Terminal: false
            );
        }

        if (
            intent == PredictionIntent.Resolve
            && remote
                is PredictionRemoteState.Active
                    or PredictionRemoteState.Locked
                    or PredictionRemoteState.Unknown
        )
        {
            return new PredictionSettlementPlan(
                ShouldResolve: true,
                ShouldCancel: false,
                ShouldRetry: false,
                ClearIntent: false,
                Terminal: false
            );
        }

        if (
            intent == PredictionIntent.Cancel
            && remote
                is PredictionRemoteState.Active
                    or PredictionRemoteState.Locked
                    or PredictionRemoteState.Unknown
        )
        {
            return new PredictionSettlementPlan(
                ShouldResolve: false,
                ShouldCancel: true,
                ShouldRetry: false,
                ClearIntent: false,
                Terminal: false
            );
        }

        return new PredictionSettlementPlan(
            ShouldResolve: false,
            ShouldCancel: false,
            ShouldRetry: true,
            ClearIntent: false,
            Terminal: false
        );
    }

    public static bool BackoffElapsed(DateTimeOffset utcNow, DateTimeOffset? nextAttemptAt) =>
        !nextAttemptAt.HasValue || utcNow >= nextAttemptAt.Value;

    public static DateTimeOffset NextRetryAt(DateTimeOffset utcNow, int failureCount)
    {
        int exponent = failureCount <= 1 ? 1 : Math.Min(failureCount, 6);
        int seconds = Math.Min(60, 1 << exponent);
        return utcNow.AddSeconds(seconds);
    }

    public static SettlementFailure Classify(Exception exception)
    {
        if (exception == null || exception is OperationCanceledException)
        {
            return SettlementFailure.None;
        }

        if (
            exception
            is BadRequestException
                or BadTokenException
                or BadScopeException
                or TokenExpiredException
                or BadResourceException
        )
        {
            return SettlementFailure.Terminal;
        }

        return SettlementFailure.Transient;
    }

    public static PredictionReconcileResult Reconcile(
        IReadOnlyList<PredictionLedgerEntry> entries,
        IReadOnlyList<PredictionRemoteRef> remote,
        bool remoteFetched
    )
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (entries != null)
        {
            foreach (PredictionLedgerEntry entry in entries)
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.PredictionId))
                {
                    known.Add(entry.PredictionId);
                }
            }
        }

        var foreign = new List<string>();
        if (remote != null)
        {
            foreach (PredictionRemoteRef item in remote)
            {
                if (
                    item.State is not (PredictionRemoteState.Active or PredictionRemoteState.Locked)
                )
                {
                    continue;
                }

                if (
                    string.IsNullOrWhiteSpace(item.PredictionId)
                    || known.Contains(item.PredictionId)
                )
                {
                    continue;
                }

                if (!foreign.Contains(item.PredictionId))
                {
                    foreign.Add(item.PredictionId);
                }
            }
        }

        var orphaned = new List<string>();
        PredictionLedgerEntry resumeEntry = null;
        if (entries != null)
        {
            foreach (PredictionLedgerEntry entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.PredictionId))
                {
                    continue;
                }

                bool found = TryFind(remote, entry.PredictionId, out PredictionRemoteRef match);
                if (
                    remoteFetched
                    && !found
                    && entry.State is PredictionLedgerState.Open or PredictionLedgerState.Pending
                )
                {
                    orphaned.Add(entry.PredictionId);
                }

                if (entry.State != PredictionLedgerState.Open || !found)
                {
                    continue;
                }

                if (
                    match.State
                    is not (PredictionRemoteState.Active or PredictionRemoteState.Locked)
                )
                {
                    continue;
                }

                if (
                    resumeEntry == null
                    || entry.CreatedAt > resumeEntry.CreatedAt
                    || (
                        entry.CreatedAt == resumeEntry.CreatedAt
                        && entry.Attempt > resumeEntry.Attempt
                    )
                )
                {
                    resumeEntry = entry;
                }
            }
        }

        PredictionResume resume = null;
        if (resumeEntry != null)
        {
            resume = new PredictionResume
            {
                ReplayId = resumeEntry.ReplayId,
                Attempt = resumeEntry.Attempt,
                PredictionId = resumeEntry.PredictionId,
                Map = resumeEntry.Map,
                OpenedAt = resumeEntry.CreatedAt,
            };
        }

        return new PredictionReconcileResult(resume, foreign, orphaned);
    }

    private static bool TryFind(
        IReadOnlyList<PredictionRemoteRef> remote,
        string predictionId,
        out PredictionRemoteRef found
    )
    {
        if (remote != null)
        {
            for (int i = 0; i < remote.Count; i++)
            {
                if (string.Equals(remote[i].PredictionId, predictionId, StringComparison.Ordinal))
                {
                    found = remote[i];
                    return true;
                }
            }
        }

        found = default;
        return false;
    }
}
