using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.YouTube.Outbox;

public sealed class SavedDispatch
{
    public UploadAttemptResult Result { get; init; }
    public bool AlreadySettled { get; init; }

    /// <summary>
    /// True only when this call moved the attempt to Uploading and the caller may insert once.
    /// An interrupted send sets this only for a retry (<c>operatorRetry</c>): the uploader's own
    /// bounded automatic retry (<see cref="InterruptedUpload"/>) or an operator's.
    /// </summary>
    public bool MaySend { get; init; }
}

public sealed class UploadOutbox
{
    private readonly UploadAttemptStore store;

    public UploadOutbox(string attemptsRoot)
    {
        store = new UploadAttemptStore(attemptsRoot);
    }

    public string Root
    {
        get { return store.Root; }
    }

    public string AttemptDirectory(string attemptId)
    {
        return store.AttemptDirectory(attemptId);
    }

    public string ManifestPath(string attemptId)
    {
        string directory = store.AttemptDirectory(attemptId);
        if (directory == null)
        {
            return null;
        }

        return Path.Combine(directory, UploadAttemptStore.ManifestFileName);
    }

    public Task<UploadAttemptResult> PrepareAsync(
        string attemptId,
        int? replayId,
        DateTimeOffset at,
        CancellationToken cancellationToken
    )
    {
        UploadAttemptResult prepared = UploadAttemptMachine.Prepare(attemptId, replayId, at);
        if (!prepared.Succeeded)
        {
            return Task.FromResult(prepared);
        }

        return store.CreateAsync(prepared.Manifest, cancellationToken);
    }

    public Task<UploadAttemptResult> BeginRecordingAsync(
        string attemptId,
        DateTimeOffset at,
        CancellationToken cancellationToken
    )
    {
        return store.UpdateAsync(
            attemptId,
            current => UploadAttemptMachine.BeginRecording(current, at),
            cancellationToken
        );
    }

    public Task<UploadAttemptResult> FinalizeMediaAsync(
        string attemptId,
        string mediaPath,
        long mediaSize,
        string mediaHash,
        DateTimeOffset at,
        CancellationToken cancellationToken
    )
    {
        return store.UpdateAsync(
            attemptId,
            current =>
                UploadAttemptMachine.FinalizeMedia(current, mediaPath, mediaSize, mediaHash, at),
            cancellationToken
        );
    }

    public Task<UploadAttemptResult> MarkUploadPendingAsync(
        string attemptId,
        DateTimeOffset at,
        CancellationToken cancellationToken
    )
    {
        return store.UpdateAsync(
            attemptId,
            current => UploadAttemptMachine.MarkUploadPending(current, at),
            cancellationToken
        );
    }

    public Task<UploadAttemptResult> DispatchAsync(
        string attemptId,
        bool youtubeEnabled,
        bool dryRun,
        bool operatorRetry,
        DateTimeOffset at,
        CancellationToken cancellationToken
    )
    {
        return store.UpdateAsync(
            attemptId,
            current =>
                UploadAttemptMachine.Dispatch(current, youtubeEnabled, dryRun, operatorRetry, at),
            cancellationToken
        );
    }

    public Task<string> ContextForReplayAsync(
        int? replayId,
        DateTimeOffset at,
        CancellationToken cancellationToken
    )
    {
        return ContextForReplayAsync(store, replayId, at, cancellationToken);
    }

    private static async Task<string> ContextForReplayAsync(
        UploadAttemptStore store,
        int? replayId,
        DateTimeOffset at,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<UploadAttemptManifest> open = await store
            .ListOpenAsync(cancellationToken)
            .ConfigureAwait(false);
        return UploadAttemptIds.SelectContext(open, replayId, at);
    }

    public Task<UploadAttemptResult> NoteSessionAsync(
        string attemptId,
        string sessionUri,
        DateTimeOffset at,
        CancellationToken cancellationToken
    )
    {
        return store.UpdateAsync(
            attemptId,
            current => UploadAttemptMachine.NoteSession(current, sessionUri, at),
            cancellationToken
        );
    }

    public Task<UploadAttemptResult> MarkAmbiguousAsync(
        string attemptId,
        DateTimeOffset at,
        CancellationToken cancellationToken
    )
    {
        return store.UpdateAsync(
            attemptId,
            current => UploadAttemptMachine.MarkAmbiguous(current, at),
            cancellationToken
        );
    }

    public Task<UploadAttemptResult> ReturnUnsentAsync(
        string attemptId,
        DateTimeOffset at,
        CancellationToken cancellationToken
    )
    {
        return store.UpdateAsync(
            attemptId,
            current => UploadAttemptMachine.ReturnUnsent(current, at),
            cancellationToken
        );
    }

    public Task<UploadAttemptResult> AbandonSessionAsync(
        string attemptId,
        DateTimeOffset at,
        CancellationToken cancellationToken
    )
    {
        return store.UpdateAsync(
            attemptId,
            current => UploadAttemptMachine.AbandonSession(current, at),
            cancellationToken
        );
    }

    public Task<UploadAttemptResult> CompleteAsync(
        string attemptId,
        string videoId,
        DateTimeOffset at,
        CancellationToken cancellationToken
    )
    {
        return store.UpdateAsync(
            attemptId,
            current => UploadAttemptMachine.Complete(current, videoId, at),
            cancellationToken
        );
    }

    public Task<UploadAttemptResult> ReconcileAsync(
        string attemptId,
        string videoId,
        DateTimeOffset at,
        CancellationToken cancellationToken
    )
    {
        return store.UpdateAsync(
            attemptId,
            current => UploadAttemptMachine.Reconcile(current, videoId, at),
            cancellationToken
        );
    }

    public Task<UploadAttemptResult> LoadAsync(
        string attemptId,
        CancellationToken cancellationToken
    )
    {
        return store.LoadAsync(attemptId, cancellationToken);
    }

    public Task<UploadAttemptResult> SavePolicyAsync(
        string attemptId,
        int? replayId,
        DateTimeOffset at,
        UploadAttemptPolicy policy,
        bool replaceOpen,
        CancellationToken cancellationToken,
        bool replaceEvaluated = false
    )
    {
        return store.SavePolicyAsync(
            attemptId,
            replayId,
            at,
            policy,
            replaceOpen,
            cancellationToken,
            replaceEvaluated
        );
    }

    public Task<UploadAttemptResult> AdvanceFromAsync(
        string attemptId,
        long observedRevision,
        Func<UploadAttemptManifest, UploadAttemptResult> transition,
        CancellationToken cancellationToken
    )
    {
        return store.AdvanceFromAsync(attemptId, observedRevision, transition, cancellationToken);
    }

    public Task<IReadOnlyList<UploadAttemptManifest>> ListOpenAsync(
        CancellationToken cancellationToken
    )
    {
        return store.ListOpenAsync(cancellationToken);
    }

    public async Task<string> SelectRestartMediaAsync(
        string attemptId,
        CancellationToken cancellationToken
    )
    {
        UploadAttemptResult loaded = await LoadAsync(attemptId, cancellationToken)
            .ConfigureAwait(false);
        if (!loaded.Succeeded)
        {
            return null;
        }

        return UploadAttemptMachine.SelectBoundMedia(loaded.Manifest);
    }

    /// <summary>
    /// Persists the same walk <see cref="UploadDispatch.ForFile"/> decides in memory.
    /// A manifest that already left the pending state is returned unchanged.
    /// </summary>
    public async Task<SavedDispatch> SaveDispatchAsync(
        string attemptId,
        int? replayId,
        string mediaPath,
        long mediaSize,
        string mediaHash,
        bool youtubeEnabled,
        bool dryRun,
        DateTimeOffset at,
        CancellationToken cancellationToken,
        bool operatorRetry = false
    )
    {
        UploadAttemptResult preview = UploadDispatch.ForFile(
            attemptId,
            replayId,
            mediaPath,
            mediaSize,
            mediaHash,
            youtubeEnabled,
            dryRun,
            at
        );
        if (!preview.Succeeded)
        {
            return Held(preview);
        }

        UploadAttemptResult loaded = await LoadAsync(attemptId, cancellationToken)
            .ConfigureAwait(false);
        if (!loaded.Succeeded && loaded.Reason != UploadAttemptReasons.ManifestMissing)
        {
            return Held(loaded);
        }

        if (loaded.Succeeded && loaded.Manifest.State == UploadAttemptState.Uploading)
        {
            return await ResumeInterruptedSendAsync(
                    attemptId,
                    loaded,
                    operatorRetry,
                    youtubeEnabled,
                    dryRun,
                    at,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        if (loaded.Succeeded && loaded.Manifest.State == UploadAttemptState.AmbiguousUpload)
        {
            return await RetryAmbiguousSendAsync(
                    attemptId,
                    loaded,
                    operatorRetry,
                    youtubeEnabled,
                    dryRun,
                    at,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        if (loaded.Succeeded && IsSettled(loaded.Manifest.State))
        {
            return Settled(loaded);
        }

        if (!loaded.Succeeded)
        {
            UploadAttemptResult created = await PrepareAsync(
                    attemptId,
                    replayId,
                    at,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (!created.Succeeded)
            {
                return Held(created);
            }

            loaded = created;
        }

        if (loaded.Manifest.State == UploadAttemptState.Prepared)
        {
            loaded = await BeginRecordingAsync(attemptId, at, cancellationToken)
                .ConfigureAwait(false);
        }

        if (loaded.Succeeded && loaded.Manifest.State == UploadAttemptState.Recording)
        {
            loaded = await FinalizeMediaAsync(
                    attemptId,
                    mediaPath,
                    mediaSize,
                    mediaHash,
                    at,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        if (loaded.Succeeded && loaded.Manifest.State == UploadAttemptState.MediaFinalized)
        {
            loaded = await MarkUploadPendingAsync(attemptId, at, cancellationToken)
                .ConfigureAwait(false);
        }

        if (loaded.Succeeded && loaded.Manifest.State == UploadAttemptState.UploadPending)
        {
            loaded = await DispatchAsync(
                    attemptId,
                    youtubeEnabled,
                    dryRun,
                    operatorRetry: false,
                    at,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return Held(loaded);
    }

    private async Task<SavedDispatch> ResumeInterruptedSendAsync(
        string attemptId,
        UploadAttemptResult loaded,
        bool operatorRetry,
        bool youtubeEnabled,
        bool dryRun,
        DateTimeOffset at,
        CancellationToken cancellationToken
    )
    {
        if (UploadAttemptReceipt.HasExactText(loaded.Manifest.VideoId))
        {
            UploadAttemptResult completed = await CompleteAsync(
                    attemptId,
                    loaded.Manifest.VideoId,
                    at,
                    cancellationToken
                )
                .ConfigureAwait(false);
            return Settled(completed);
        }

        UploadAttemptResult ambiguous = await MarkAmbiguousAsync(attemptId, at, cancellationToken)
            .ConfigureAwait(false);
        if (!ambiguous.Succeeded || !operatorRetry)
        {
            return Held(ambiguous);
        }

        return await RetryAmbiguousSendAsync(
                attemptId,
                ambiguous,
                operatorRetry: true,
                youtubeEnabled,
                dryRun,
                at,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task<SavedDispatch> RetryAmbiguousSendAsync(
        string attemptId,
        UploadAttemptResult loaded,
        bool operatorRetry,
        bool youtubeEnabled,
        bool dryRun,
        DateTimeOffset at,
        CancellationToken cancellationToken
    )
    {
        if (!operatorRetry)
        {
            return Settled(loaded);
        }

        UploadAttemptResult retried = await DispatchAsync(
                attemptId,
                youtubeEnabled,
                dryRun,
                operatorRetry: true,
                at,
                cancellationToken
            )
            .ConfigureAwait(false);
        return Held(retried);
    }

    private static SavedDispatch Settled(UploadAttemptResult result)
    {
        return new SavedDispatch
        {
            Result = result,
            AlreadySettled = true,
            MaySend = false,
        };
    }

    private static SavedDispatch Held(UploadAttemptResult result)
    {
        return new SavedDispatch
        {
            Result = result,
            AlreadySettled = false,
            MaySend = result.Succeeded && result.Manifest?.State == UploadAttemptState.Uploading,
        };
    }

    private static bool IsSettled(UploadAttemptState state)
    {
        return state == UploadAttemptState.DryRunSimulated
            || state == UploadAttemptState.Disabled
            || state == UploadAttemptState.Uploaded;
    }
}
