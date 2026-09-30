using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.YouTube.Outbox;

public sealed class SavedDispatch
{
    public UploadAttemptResult Result { get; init; }
    public bool AlreadySettled { get; init; }
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
        CancellationToken cancellationToken
    )
    {
        return store.SavePolicyAsync(
            attemptId,
            replayId,
            at,
            policy,
            replaceOpen,
            cancellationToken
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
        CancellationToken cancellationToken
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
            return new SavedDispatch { Result = preview, AlreadySettled = false };
        }

        UploadAttemptResult loaded = await LoadAsync(attemptId, cancellationToken)
            .ConfigureAwait(false);
        if (!loaded.Succeeded && loaded.Reason != UploadAttemptReasons.ManifestMissing)
        {
            return new SavedDispatch { Result = loaded, AlreadySettled = false };
        }

        if (loaded.Succeeded && IsSettled(loaded.Manifest.State))
        {
            return new SavedDispatch { Result = loaded, AlreadySettled = true };
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
                return new SavedDispatch { Result = created, AlreadySettled = false };
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

        return new SavedDispatch { Result = loaded, AlreadySettled = false };
    }

    private static bool IsSettled(UploadAttemptState state)
    {
        return state == UploadAttemptState.DryRunSimulated
            || state == UploadAttemptState.Disabled
            || state == UploadAttemptState.Uploading
            || state == UploadAttemptState.Uploaded
            || state == UploadAttemptState.AmbiguousUpload;
    }
}
