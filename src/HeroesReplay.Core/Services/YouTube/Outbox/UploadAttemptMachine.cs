using System;

namespace HeroesReplay.Core.Services.YouTube.Outbox;

public static class UploadAttemptMachine
{
    public static UploadAttemptResult Prepare(string attemptId, int? replayId, DateTimeOffset at)
    {
        if (!IsUtc(at))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ClockNotUtc, null);
        }

        if (string.IsNullOrWhiteSpace(attemptId))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.AttemptIdRequired, null);
        }

        if (!UploadAttemptIds.IsSafe(attemptId))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.AttemptIdInvalid, null);
        }

        return UploadAttemptResult.Success(
            new UploadAttemptManifest
            {
                Schema = UploadAttemptManifest.SchemaVersion,
                AttemptId = attemptId,
                ReplayId = replayId,
                State = UploadAttemptState.Prepared,
                Revision = 0,
                UpdatedAtUtc = at,
            }
        );
    }

    public static UploadAttemptResult BeginRecording(
        UploadAttemptManifest current,
        DateTimeOffset at
    )
    {
        UploadAttemptResult rejected = RejectClockOrMissing(current, at);
        if (rejected != null)
        {
            return rejected;
        }

        if (current.State != UploadAttemptState.Prepared)
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.IllegalTransition, current);
        }

        return Advance(current, UploadAttemptState.Recording, at, null, 0, null, null, null, null);
    }

    public static UploadAttemptResult FinalizeMedia(
        UploadAttemptManifest current,
        string mediaPath,
        long mediaSize,
        string mediaHash,
        DateTimeOffset at
    )
    {
        UploadAttemptResult rejected = RejectClockOrMissing(current, at);
        if (rejected != null)
        {
            return rejected;
        }

        if (current.State != UploadAttemptState.Recording)
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.IllegalTransition, current);
        }

        if (!IsCompleteMedia(mediaPath, mediaSize, mediaHash))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.MediaIncomplete, current);
        }

        return Advance(
            current,
            UploadAttemptState.MediaFinalized,
            at,
            mediaPath,
            mediaSize,
            mediaHash,
            null,
            null,
            null
        );
    }

    public static UploadAttemptResult MarkUploadPending(
        UploadAttemptManifest current,
        DateTimeOffset at
    )
    {
        UploadAttemptResult rejected = RejectClockOrMissing(current, at);
        if (rejected != null)
        {
            return rejected;
        }

        if (
            current.State != UploadAttemptState.MediaFinalized
            || !UploadAttemptReceipt.IsBound(current)
        )
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.IllegalTransition, current);
        }

        return Advance(
            current,
            UploadAttemptState.UploadPending,
            at,
            current.MediaPath,
            current.MediaSize,
            current.MediaHash,
            null,
            null,
            null
        );
    }

    public static UploadAttemptResult Dispatch(
        UploadAttemptManifest current,
        bool youtubeEnabled,
        bool dryRun,
        bool operatorRetry,
        DateTimeOffset at
    )
    {
        UploadAttemptResult rejected = RejectClockOrMissing(current, at);
        if (rejected != null)
        {
            return rejected;
        }

        if (current.State == UploadAttemptState.AmbiguousUpload)
        {
            if (!UploadAttemptReceipt.IsBound(current))
            {
                return UploadAttemptResult.Failure(UploadAttemptReasons.IllegalTransition, current);
            }

            if (!operatorRetry)
            {
                return UploadAttemptResult.Failure(
                    UploadAttemptReasons.OperatorRetryRequired,
                    current
                );
            }

            if (!youtubeEnabled)
            {
                return UploadAttemptResult.Failure(UploadAttemptReasons.YoutubeDisabled, current);
            }

            if (dryRun)
            {
                return UploadAttemptResult.Failure(UploadAttemptReasons.DryRun, current);
            }

            return CopyForward(current, UploadAttemptState.Uploading, at, null);
        }

        if (
            current.State != UploadAttemptState.UploadPending
            || !UploadAttemptReceipt.IsBound(current)
        )
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.IllegalTransition, current);
        }

        if (!youtubeEnabled)
        {
            return CopyForward(current, UploadAttemptState.Disabled, at, null);
        }

        if (dryRun)
        {
            return CopyForward(
                current,
                UploadAttemptState.DryRunSimulated,
                at,
                UploadAttemptReceiptKind.Simulation
            );
        }

        return CopyForward(current, UploadAttemptState.Uploading, at, null);
    }

    public static UploadAttemptResult MarkAmbiguous(
        UploadAttemptManifest current,
        DateTimeOffset at
    )
    {
        UploadAttemptResult rejected = RejectClockOrMissing(current, at);
        if (rejected != null)
        {
            return rejected;
        }

        if (
            current.State != UploadAttemptState.Uploading
            || !UploadAttemptReceipt.IsBound(current)
            || UploadAttemptReceipt.HasExactText(current.VideoId)
        )
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.IllegalTransition, current);
        }

        return CopyForward(current, UploadAttemptState.AmbiguousUpload, at, null);
    }

    public static UploadAttemptResult NoteSession(
        UploadAttemptManifest current,
        string sessionUri,
        DateTimeOffset at
    )
    {
        UploadAttemptResult rejected = RejectClockOrMissing(current, at);
        if (rejected != null)
        {
            return rejected;
        }

        if (current.State != UploadAttemptState.Uploading || !UploadAttemptReceipt.IsBound(current))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.IllegalTransition, current);
        }

        if (!UploadAttemptIds.IsSessionUri(sessionUri))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.IllegalTransition, current);
        }

        if (
            UploadAttemptReceipt.HasExactText(current.SessionUri)
            && !string.Equals(current.SessionUri, sessionUri, StringComparison.Ordinal)
        )
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.IllegalTransition, current);
        }

        return Advance(
            current,
            UploadAttemptState.Uploading,
            at,
            current.MediaPath,
            current.MediaSize,
            current.MediaHash,
            null,
            null,
            sessionUri
        );
    }

    public static UploadAttemptResult Complete(
        UploadAttemptManifest current,
        string videoId,
        DateTimeOffset at
    )
    {
        UploadAttemptResult rejected = RejectClockOrMissing(current, at);
        if (rejected != null)
        {
            return rejected;
        }

        if (current.State == UploadAttemptState.Uploaded)
        {
            if (!UploadAttemptReceipt.HasExactText(videoId))
            {
                return UploadAttemptResult.Failure(UploadAttemptReasons.VideoIdRequired, current);
            }

            return UploadAttemptResult.Failure(
                VideoConflictReason(current.VideoId, videoId),
                current
            );
        }

        if (current.State != UploadAttemptState.Uploading || !UploadAttemptReceipt.IsBound(current))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.IllegalTransition, current);
        }

        if (!UploadAttemptReceipt.HasExactText(videoId))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.VideoIdRequired, current);
        }

        if (
            UploadAttemptReceipt.HasExactText(current.VideoId)
            && !SameText(current.VideoId, videoId)
        )
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.VideoIdConflict, current);
        }

        return CopyForward(
            current,
            UploadAttemptState.Uploaded,
            at,
            UploadAttemptReceiptKind.Production,
            videoId
        );
    }

    public static UploadAttemptResult Reconcile(
        UploadAttemptManifest current,
        string videoId,
        DateTimeOffset at
    )
    {
        UploadAttemptResult rejected = RejectClockOrMissing(current, at);
        if (rejected != null)
        {
            return rejected;
        }

        if (current.State == UploadAttemptState.Uploaded)
        {
            if (!UploadAttemptReceipt.HasExactText(videoId))
            {
                return UploadAttemptResult.Failure(UploadAttemptReasons.VideoIdRequired, current);
            }

            return UploadAttemptResult.Failure(
                VideoConflictReason(current.VideoId, videoId),
                current
            );
        }

        if (
            current.State != UploadAttemptState.AmbiguousUpload
            || !UploadAttemptReceipt.IsBound(current)
        )
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.IllegalTransition, current);
        }

        if (!UploadAttemptReceipt.HasExactText(videoId))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.VideoIdRequired, current);
        }

        if (
            UploadAttemptReceipt.HasExactText(current.VideoId)
            && !SameText(current.VideoId, videoId)
        )
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.VideoIdConflict, current);
        }

        return CopyForward(
            current,
            UploadAttemptState.Uploaded,
            at,
            UploadAttemptReceiptKind.Production,
            videoId
        );
    }

    public static string SelectBoundMedia(UploadAttemptManifest manifest)
    {
        if (!UploadAttemptReceipt.IsBound(manifest))
        {
            return null;
        }

        if (
            manifest.State == UploadAttemptState.Prepared
            || manifest.State == UploadAttemptState.Recording
        )
        {
            return null;
        }

        return manifest.MediaPath;
    }

    public static string SuccessorRejection(
        UploadAttemptManifest current,
        UploadAttemptManifest proposed
    )
    {
        if (current == null || proposed == null)
        {
            return UploadAttemptReasons.IllegalTransition;
        }

        if (
            current.Schema != UploadAttemptManifest.SchemaVersion
            || proposed.Schema != UploadAttemptManifest.SchemaVersion
            || !SameText(current.AttemptId, proposed.AttemptId)
            || current.ReplayId != proposed.ReplayId
        )
        {
            return UploadAttemptReasons.IllegalTransition;
        }

        if (!IsUtc(proposed.UpdatedAtUtc))
        {
            return UploadAttemptReasons.ClockNotUtc;
        }

        if (!IsEdge(current.State, proposed.State))
        {
            return UploadAttemptReasons.IllegalTransition;
        }

        if (!BindingMatches(current, proposed))
        {
            return UploadAttemptReasons.IllegalTransition;
        }

        if (!VideoMatches(current, proposed))
        {
            return
                UploadAttemptReceipt.HasExactText(proposed.VideoId)
                && UploadAttemptReceipt.HasExactText(current.VideoId)
                && !SameText(current.VideoId, proposed.VideoId)
                ? UploadAttemptReasons.VideoIdConflict
                : UploadAttemptReasons.IllegalTransition;
        }

        if (!ReceiptMatches(proposed))
        {
            return UploadAttemptReasons.IllegalTransition;
        }

        if (!SessionMatches(current, proposed))
        {
            return UploadAttemptReasons.IllegalTransition;
        }

        return null;
    }

    private static bool SessionMatches(
        UploadAttemptManifest current,
        UploadAttemptManifest proposed
    )
    {
        if (
            current.State == UploadAttemptState.Uploading
            && proposed.State == UploadAttemptState.Uploading
        )
        {
            if (!UploadAttemptIds.IsSessionUri(proposed.SessionUri))
            {
                return false;
            }

            return !UploadAttemptReceipt.HasExactText(current.SessionUri)
                || string.Equals(current.SessionUri, proposed.SessionUri, StringComparison.Ordinal);
        }

        return string.Equals(current.SessionUri, proposed.SessionUri, StringComparison.Ordinal);
    }

    private static UploadAttemptResult RejectClockOrMissing(
        UploadAttemptManifest current,
        DateTimeOffset at
    )
    {
        if (!IsUtc(at))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ClockNotUtc, current);
        }

        if (current == null)
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.IllegalTransition, null);
        }

        return null;
    }

    private static UploadAttemptResult CopyForward(
        UploadAttemptManifest current,
        UploadAttemptState state,
        DateTimeOffset at,
        string receiptKind
    )
    {
        return CopyForward(current, state, at, receiptKind, null);
    }

    private static UploadAttemptResult CopyForward(
        UploadAttemptManifest current,
        UploadAttemptState state,
        DateTimeOffset at,
        string receiptKind,
        string videoId
    )
    {
        return Advance(
            current,
            state,
            at,
            current.MediaPath,
            current.MediaSize,
            current.MediaHash,
            videoId,
            receiptKind,
            current.SessionUri
        );
    }

    private static UploadAttemptResult Advance(
        UploadAttemptManifest current,
        UploadAttemptState state,
        DateTimeOffset at,
        string mediaPath,
        long mediaSize,
        string mediaHash,
        string videoId,
        string receiptKind,
        string sessionUri
    )
    {
        return UploadAttemptResult.Success(
            new UploadAttemptManifest
            {
                Schema = UploadAttemptManifest.SchemaVersion,
                AttemptId = current.AttemptId,
                ReplayId = current.ReplayId,
                State = state,
                MediaPath = mediaPath,
                MediaSize = mediaSize,
                MediaHash = mediaHash,
                VideoId = videoId,
                Revision = current.Revision,
                UpdatedAtUtc = at,
                ReceiptKind = receiptKind,
                Policy = current.Policy,
                SessionUri = sessionUri,
            }
        );
    }

    private static bool IsEdge(UploadAttemptState from, UploadAttemptState to)
    {
        switch (from)
        {
            case UploadAttemptState.Prepared:
                return to == UploadAttemptState.Recording;
            case UploadAttemptState.Recording:
                return to == UploadAttemptState.MediaFinalized;
            case UploadAttemptState.MediaFinalized:
                return to == UploadAttemptState.UploadPending;
            case UploadAttemptState.UploadPending:
                return to == UploadAttemptState.Uploading
                    || to == UploadAttemptState.DryRunSimulated
                    || to == UploadAttemptState.Disabled;
            case UploadAttemptState.Uploading:
                return to == UploadAttemptState.Uploaded
                    || to == UploadAttemptState.AmbiguousUpload
                    || to == UploadAttemptState.Uploading;
            case UploadAttemptState.AmbiguousUpload:
                return to == UploadAttemptState.Uploading || to == UploadAttemptState.Uploaded;
            default:
                return false;
        }
    }

    private static bool BindingMatches(
        UploadAttemptManifest current,
        UploadAttemptManifest proposed
    )
    {
        if (
            current.State == UploadAttemptState.Recording
            && proposed.State == UploadAttemptState.MediaFinalized
        )
        {
            return IsFullyUnbound(current) && UploadAttemptReceipt.IsBound(proposed);
        }

        if (IsUnboundState(current.State) || IsUnboundState(proposed.State))
        {
            return IsFullyUnbound(current) && IsFullyUnbound(proposed);
        }

        return UploadAttemptReceipt.IsBound(current)
            && UploadAttemptReceipt.IsBound(proposed)
            && SameText(current.MediaPath, proposed.MediaPath)
            && current.MediaSize == proposed.MediaSize
            && SameText(current.MediaHash, proposed.MediaHash);
    }

    private static bool VideoMatches(UploadAttemptManifest current, UploadAttemptManifest proposed)
    {
        if (proposed.State == UploadAttemptState.Uploaded)
        {
            if (!UploadAttemptReceipt.HasExactText(proposed.VideoId))
            {
                return false;
            }

            if (!UploadAttemptReceipt.HasExactText(current.VideoId))
            {
                return true;
            }

            return SameText(current.VideoId, proposed.VideoId);
        }

        return !UploadAttemptReceipt.HasExactText(current.VideoId)
            && !UploadAttemptReceipt.HasExactText(proposed.VideoId);
    }

    private static bool ReceiptMatches(UploadAttemptManifest proposed)
    {
        if (proposed.State == UploadAttemptState.Uploaded)
        {
            return SameText(proposed.ReceiptKind, UploadAttemptReceiptKind.Production);
        }

        if (proposed.State == UploadAttemptState.DryRunSimulated)
        {
            return SameText(proposed.ReceiptKind, UploadAttemptReceiptKind.Simulation);
        }

        return !UploadAttemptReceipt.HasExactText(proposed.ReceiptKind);
    }

    private static bool IsUnboundState(UploadAttemptState state)
    {
        return state == UploadAttemptState.Prepared || state == UploadAttemptState.Recording;
    }

    private static bool IsFullyUnbound(UploadAttemptManifest manifest)
    {
        return manifest.MediaSize == 0
            && !UploadAttemptReceipt.HasExactText(manifest.MediaPath)
            && !UploadAttemptReceipt.HasExactText(manifest.MediaHash);
    }

    private static bool IsCompleteMedia(string mediaPath, long mediaSize, string mediaHash)
    {
        return UploadAttemptReceipt.HasExactText(mediaPath)
            && mediaSize > 0
            && UploadAttemptReceipt.HasExactText(mediaHash);
    }

    private static string VideoConflictReason(string currentVideoId, string videoId)
    {
        return SameText(currentVideoId, videoId)
            ? UploadAttemptReasons.AlreadyUploaded
            : UploadAttemptReasons.VideoIdConflict;
    }

    private static bool IsUtc(DateTimeOffset at)
    {
        return at.Offset == TimeSpan.Zero;
    }

    private static bool SameText(string left, string right)
    {
        return string.Equals(left, right, StringComparison.Ordinal);
    }
}
