using System;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Services.YouTube.Outbox;

public sealed class UploadAttemptManifest
{
    public const int SchemaVersion = 1;
    public const string SchemaProperty = "Schema";
    public const string AttemptIdProperty = "AttemptId";
    public const string ReplayIdProperty = "ReplayId";
    public const string StateProperty = "State";
    public const string MediaPathProperty = "MediaPath";
    public const string MediaSizeProperty = "MediaSize";
    public const string MediaHashProperty = "MediaHash";
    public const string VideoIdProperty = "VideoId";
    public const string RevisionProperty = "Revision";
    public const string UpdatedAtUtcProperty = "UpdatedAtUtc";
    public const string ReceiptKindProperty = "ReceiptKind";
    public const string PolicyProperty = "Policy";

    [JsonPropertyName(SchemaProperty)]
    public int Schema { get; init; }

    [JsonPropertyName(AttemptIdProperty)]
    public string AttemptId { get; init; }

    [JsonPropertyName(ReplayIdProperty)]
    public int? ReplayId { get; init; }

    [JsonPropertyName(StateProperty)]
    public UploadAttemptState State { get; init; }

    [JsonPropertyName(MediaPathProperty)]
    public string MediaPath { get; init; }

    [JsonPropertyName(MediaSizeProperty)]
    public long MediaSize { get; init; }

    [JsonPropertyName(MediaHashProperty)]
    public string MediaHash { get; init; }

    [JsonPropertyName(VideoIdProperty)]
    public string VideoId { get; init; }

    [JsonPropertyName(RevisionProperty)]
    public long Revision { get; init; }

    [JsonPropertyName(UpdatedAtUtcProperty)]
    public DateTimeOffset UpdatedAtUtc { get; init; }

    [JsonPropertyName(ReceiptKindProperty)]
    public string ReceiptKind { get; init; }

    /// <summary>Null on manifests written before a policy snapshot existed.</summary>
    [JsonPropertyName(PolicyProperty)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UploadAttemptPolicy Policy { get; init; }

    public UploadAttemptManifest WithRevision(long revision)
    {
        return new UploadAttemptManifest
        {
            Schema = Schema,
            AttemptId = AttemptId,
            ReplayId = ReplayId,
            State = State,
            MediaPath = MediaPath,
            MediaSize = MediaSize,
            MediaHash = MediaHash,
            VideoId = VideoId,
            Revision = revision,
            UpdatedAtUtc = UpdatedAtUtc,
            ReceiptKind = ReceiptKind,
            Policy = Policy,
        };
    }

    public UploadAttemptManifest WithPolicy(UploadAttemptPolicy policy)
    {
        return new UploadAttemptManifest
        {
            Schema = Schema,
            AttemptId = AttemptId,
            ReplayId = ReplayId,
            State = State,
            MediaPath = MediaPath,
            MediaSize = MediaSize,
            MediaHash = MediaHash,
            VideoId = VideoId,
            Revision = Revision,
            UpdatedAtUtc = UpdatedAtUtc,
            ReceiptKind = ReceiptKind,
            Policy = policy,
        };
    }
}

public static class UploadAttemptReceipt
{
    public static bool IsProduction(UploadAttemptManifest manifest)
    {
        return manifest != null
            && manifest.State == UploadAttemptState.Uploaded
            && string.Equals(
                manifest.ReceiptKind,
                UploadAttemptReceiptKind.Production,
                StringComparison.Ordinal
            )
            && HasExactText(manifest.VideoId)
            && IsBound(manifest);
    }

    public static bool IsSimulation(UploadAttemptManifest manifest)
    {
        return manifest != null
            && manifest.State == UploadAttemptState.DryRunSimulated
            && string.Equals(
                manifest.ReceiptKind,
                UploadAttemptReceiptKind.Simulation,
                StringComparison.Ordinal
            )
            && !HasExactText(manifest.VideoId)
            && IsBound(manifest);
    }

    public static bool IsBound(UploadAttemptManifest manifest)
    {
        return manifest != null
            && HasExactText(manifest.MediaPath)
            && manifest.MediaSize > 0
            && HasExactText(manifest.MediaHash);
    }

    public static bool HasExactText(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && string.Equals(value, value.Trim(), StringComparison.Ordinal);
    }
}
