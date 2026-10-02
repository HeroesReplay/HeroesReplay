using System;

namespace HeroesReplay.Core.YouTube.Outbox;

/// <summary>
/// Walks one finished recording to the dispatch decision. The uploader acts on that result.
/// </summary>
public static class UploadDispatch
{
    public static UploadAttemptResult ForFile(
        string attemptId,
        int? replayId,
        string mediaPath,
        long mediaSize,
        string mediaHash,
        bool youtubeEnabled,
        bool dryRun,
        DateTimeOffset at
    )
    {
        UploadAttemptResult prepared = UploadAttemptMachine.Prepare(attemptId, replayId, at);
        if (!prepared.Succeeded)
        {
            return prepared;
        }

        UploadAttemptResult recording = UploadAttemptMachine.BeginRecording(prepared.Manifest, at);
        if (!recording.Succeeded)
        {
            return recording;
        }

        UploadAttemptResult finalized = UploadAttemptMachine.FinalizeMedia(
            recording.Manifest,
            mediaPath,
            mediaSize,
            mediaHash,
            at
        );
        if (!finalized.Succeeded)
        {
            return finalized;
        }

        UploadAttemptResult pending = UploadAttemptMachine.MarkUploadPending(
            finalized.Manifest,
            at
        );
        if (!pending.Succeeded)
        {
            return pending;
        }

        return UploadAttemptMachine.Dispatch(
            pending.Manifest,
            youtubeEnabled,
            dryRun,
            operatorRetry: false,
            at
        );
    }
}
