namespace HeroesReplay.Core.Services.YouTube.Outbox;

public sealed class UploadAttemptResult
{
    private UploadAttemptResult(bool succeeded, string reason, UploadAttemptManifest manifest)
    {
        Succeeded = succeeded;
        Reason = reason;
        Manifest = manifest;
    }

    public bool Succeeded { get; }

    public string Reason { get; }

    public UploadAttemptManifest Manifest { get; }

    public static UploadAttemptResult Success(UploadAttemptManifest manifest)
    {
        return new UploadAttemptResult(true, UploadAttemptReasons.Ok, manifest);
    }

    public static UploadAttemptResult Failure(string reason, UploadAttemptManifest manifest)
    {
        return new UploadAttemptResult(false, reason, manifest);
    }
}
