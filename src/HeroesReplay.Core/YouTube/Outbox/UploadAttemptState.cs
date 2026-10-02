namespace HeroesReplay.Core.YouTube.Outbox;

public enum UploadAttemptState
{
    Prepared = 0,
    Recording = 1,
    MediaFinalized = 2,
    UploadPending = 3,
    Uploading = 4,
    Uploaded = 5,
    DryRunSimulated = 6,
    Disabled = 7,
    AmbiguousUpload = 8,
}
