using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.YouTube.Outbox;

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
}
