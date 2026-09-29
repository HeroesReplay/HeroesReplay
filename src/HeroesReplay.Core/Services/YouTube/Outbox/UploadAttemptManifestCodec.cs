using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Services.YouTube.Outbox;

public static class UploadAttemptManifestCodec
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static string Write(UploadAttemptManifest manifest)
    {
        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    /// <summary>
    /// Unknown properties are ignored so a later field does not make an existing receipt unreadable.
    /// A document that claims Uploaded without one video id and bound media is corrupt.
    /// </summary>
    public static UploadAttemptResult Read(string json, string expectedAttemptId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestCorrupt, null);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestCorrupt, null);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestCorrupt, null);
            }

            return ReadObject(document.RootElement, expectedAttemptId);
        }
    }

    private static UploadAttemptResult ReadObject(JsonElement root, string expectedAttemptId)
    {
        if (
            !TryInt(root, UploadAttemptManifest.SchemaProperty, out int schema)
            || schema != UploadAttemptManifest.SchemaVersion
        )
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestCorrupt, null);
        }

        if (!TryRequiredString(root, UploadAttemptManifest.AttemptIdProperty, out string attemptId))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestCorrupt, null);
        }

        if (
            !UploadAttemptIds.IsSafe(attemptId)
            || (
                expectedAttemptId != null
                && !string.Equals(attemptId, expectedAttemptId, StringComparison.Ordinal)
            )
        )
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestCorrupt, null);
        }

        if (!TryState(root, out UploadAttemptState state))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestCorrupt, null);
        }

        if (!TryReplayId(root, out int? replayId))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestCorrupt, null);
        }

        if (!TryOptionalString(root, UploadAttemptManifest.MediaPathProperty, out string mediaPath))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestCorrupt, null);
        }

        if (!TryOptionalString(root, UploadAttemptManifest.MediaHashProperty, out string mediaHash))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestCorrupt, null);
        }

        if (!TryOptionalString(root, UploadAttemptManifest.VideoIdProperty, out string videoId))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestCorrupt, null);
        }

        if (
            !TryOptionalString(
                root,
                UploadAttemptManifest.ReceiptKindProperty,
                out string receiptKind
            )
        )
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestCorrupt, null);
        }

        if (!TryMediaSize(root, out long mediaSize))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestCorrupt, null);
        }

        if (
            !TryInt64(root, UploadAttemptManifest.RevisionProperty, out long revision)
            || revision < 1
        )
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestCorrupt, null);
        }

        if (!TryTimestamp(root, out DateTimeOffset updatedAtUtc))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestCorrupt, null);
        }

        var manifest = new UploadAttemptManifest
        {
            Schema = schema,
            AttemptId = attemptId,
            ReplayId = replayId,
            State = state,
            MediaPath = EmptyToNull(mediaPath),
            MediaSize = mediaSize,
            MediaHash = EmptyToNull(mediaHash),
            VideoId = EmptyToNull(videoId),
            Revision = revision,
            UpdatedAtUtc = updatedAtUtc,
            ReceiptKind = EmptyToNull(receiptKind),
        };

        if (!IsInternallyConsistent(manifest))
        {
            return UploadAttemptResult.Failure(UploadAttemptReasons.ManifestCorrupt, null);
        }

        return UploadAttemptResult.Success(manifest);
    }

    private static bool IsInternallyConsistent(UploadAttemptManifest manifest)
    {
        bool early =
            manifest.State == UploadAttemptState.Prepared
            || manifest.State == UploadAttemptState.Recording;
        if (early)
        {
            return !UploadAttemptReceipt.IsBound(manifest)
                && manifest.MediaSize == 0
                && !UploadAttemptReceipt.HasExactText(manifest.MediaPath)
                && !UploadAttemptReceipt.HasExactText(manifest.MediaHash)
                && !UploadAttemptReceipt.HasExactText(manifest.VideoId)
                && !UploadAttemptReceipt.HasExactText(manifest.ReceiptKind);
        }

        if (!UploadAttemptReceipt.IsBound(manifest))
        {
            return false;
        }

        if (manifest.State == UploadAttemptState.Uploaded)
        {
            return UploadAttemptReceipt.IsProduction(manifest);
        }

        if (manifest.State == UploadAttemptState.DryRunSimulated)
        {
            return UploadAttemptReceipt.IsSimulation(manifest);
        }

        return !UploadAttemptReceipt.HasExactText(manifest.VideoId)
            && !UploadAttemptReceipt.HasExactText(manifest.ReceiptKind);
    }

    private static bool TryState(JsonElement root, out UploadAttemptState state)
    {
        state = default;
        if (!root.TryGetProperty(UploadAttemptManifest.StateProperty, out JsonElement property))
        {
            return false;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string text = property.GetString();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (
            !Enum.TryParse(text, ignoreCase: false, out state)
            || !Enum.IsDefined(typeof(UploadAttemptState), state)
        )
        {
            return false;
        }

        return string.Equals(text, state.ToString(), StringComparison.Ordinal);
    }

    private static bool TryReplayId(JsonElement root, out int? replayId)
    {
        replayId = null;
        if (!root.TryGetProperty(UploadAttemptManifest.ReplayIdProperty, out JsonElement property))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out int parsed))
        {
            return false;
        }

        replayId = parsed;
        return true;
    }

    private static bool TryMediaSize(JsonElement root, out long mediaSize)
    {
        mediaSize = 0;
        if (!root.TryGetProperty(UploadAttemptManifest.MediaSizeProperty, out JsonElement property))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out mediaSize))
        {
            return false;
        }

        return true;
    }

    private static bool TryTimestamp(JsonElement root, out DateTimeOffset updatedAtUtc)
    {
        updatedAtUtc = default;
        if (
            !root.TryGetProperty(
                UploadAttemptManifest.UpdatedAtUtcProperty,
                out JsonElement property
            )
            || property.ValueKind != JsonValueKind.String
            || !property.TryGetDateTimeOffset(out updatedAtUtc)
        )
        {
            return false;
        }

        return updatedAtUtc.Offset == TimeSpan.Zero;
    }

    private static bool TryRequiredString(JsonElement root, string name, out string value)
    {
        value = null;
        if (
            !root.TryGetProperty(name, out JsonElement property)
            || property.ValueKind != JsonValueKind.String
        )
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryOptionalString(JsonElement root, string name, out string value)
    {
        value = null;
        if (!root.TryGetProperty(name, out JsonElement property))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static bool TryInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static bool TryInt64(JsonElement root, string name, out long value)
    {
        value = 0;
        return root.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out value);
    }

    private static string EmptyToNull(string value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter(null, allowIntegerValues: false));
        return options;
    }
}
