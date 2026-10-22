using System;
using System.IO;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Retention;

namespace HeroesReplay.Core.YouTube.Outbox;

/// <summary>
/// Upload attempt folders that no longer matter (#492). The media policy opens one durable
/// folder per evaluated replay, recorded or not, and nothing removed one: 1,567 folders on the
/// stream PC, about 70 a day, 1,251 of them <c>Prepared</c> with <c>Record=false</c>. A folder
/// goes once its manifest is <c>Retention:AttemptKeepDays</c> old and its state is terminal:
/// uploaded, simulated, disabled, never recorded, or prepared but with its recording gone. One that
/// is sending or may have sent (<c>UploadPending</c>, <c>Uploading</c>, <c>AmbiguousUpload</c>),
/// one whose recording is still on disk, and one whose manifest does not read are kept.
/// </summary>
public static class UploadAttemptCleanup
{
    public const string PrunerName = "upload-attempts";

    public static IPruner Pruner(AppSettings settings) =>
        new Pruner(
            PrunerName,
            "Upload attempt folders in a terminal state (uploaded, simulated, disabled, never recorded, or recording gone) after Retention:AttemptKeepDays. Pending, uploading and ambiguous attempts stay.",
            now =>
                Plan(
                    PendingYouTubeUpload.AttemptsDirectory(settings?.Location?.DataDirectory),
                    string.IsNullOrWhiteSpace(settings?.Location?.DataDirectory)
                        ? null
                        : settings.ContextsDirectory,
                    (settings?.Retention ?? new RetentionSettings()).EffectiveAttemptKeepDays,
                    now
                )
        );

    /// <summary>
    /// What the pruner removes now. <paramref name="contextsDirectory"/> holds each replay's
    /// folder (<c>Contexts\&lt;replay id&gt;</c>): a prepared attempt whose replay still has a
    /// recording or a waiting entry there is not over. Writes nothing.
    /// </summary>
    public static PrunePlan Plan(
        string attemptsRoot,
        string contextsDirectory,
        int keepDays,
        DateTimeOffset utcNow
    )
    {
        var plan = new PrunePlan(PrunerName);
        if (string.IsNullOrWhiteSpace(attemptsRoot) || !Directory.Exists(attemptsRoot))
        {
            return plan;
        }

        DateTimeOffset before = utcNow.AddDays(-Math.Max(1, keepDays));
        foreach (DirectoryInfo folder in new DirectoryInfo(attemptsRoot).GetDirectories())
        {
            var manifestFile = new FileInfo(
                Path.Combine(folder.FullName, UploadAttemptStore.ManifestFileName)
            );
            if (!manifestFile.Exists)
            {
                continue;
            }

            UploadAttemptManifest manifest = Read(manifestFile.FullName, folder.Name);
            if (manifest == null)
            {
                continue;
            }

            DateTimeOffset updated =
                manifest.UpdatedAtUtc > DateTimeOffset.MinValue
                    ? manifest.UpdatedAtUtc
                    : manifestFile.LastWriteTimeUtc;
            if (updated >= before)
            {
                continue;
            }

            string terminal = TerminalReason(manifest);
            if (terminal != null && !ContextWaits(contextsDirectory, manifest))
            {
                plan.Add(
                    PruneItem.ForDirectory(
                        folder,
                        $"{terminal}, last updated more than Retention:AttemptKeepDays ({keepDays}) ago"
                    )
                );
            }
        }

        return plan;
    }

    /// <summary>
    /// Why the attempt is over, or null while it may still matter: a send under way or of unknown
    /// outcome, or a recording still on disk that an uploader could send.
    /// </summary>
    public static string TerminalReason(UploadAttemptManifest manifest)
    {
        if (manifest == null)
        {
            return null;
        }

        switch (manifest.State)
        {
            case UploadAttemptState.UploadPending:
            case UploadAttemptState.Uploading:
            case UploadAttemptState.AmbiguousUpload:
                return null;
        }

        if (MediaExists(manifest.MediaPath))
        {
            return null;
        }

        return manifest.State switch
        {
            UploadAttemptState.Uploaded => "uploaded",
            UploadAttemptState.DryRunSimulated => "simulated by a dry run",
            UploadAttemptState.Disabled => "disabled",
            _ when manifest.Policy?.Record == false =>
                $"never recorded ({manifest.Policy.RecordingReason ?? "no reason"})",
            _ => $"{manifest.State} with no recording left",
        };
    }

    /// <summary>
    /// True while the replay's context still has a recording or a <c>youtube-entry.json</c> that
    /// waits for its insert: a prepared attempt carries the media decision the uploader sends it
    /// by, and its <c>MediaPath</c> is not set until the media is bound.
    /// </summary>
    private static bool ContextWaits(string contextsDirectory, UploadAttemptManifest manifest)
    {
        if (
            string.IsNullOrWhiteSpace(contextsDirectory)
            || manifest.ReplayId is not int replayId
            || replayId <= 0
            || manifest.State is UploadAttemptState.Uploaded or UploadAttemptState.Disabled
        )
        {
            return false;
        }

        string context = Path.Combine(
            contextsDirectory,
            replayId.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        try
        {
            return Directory.Exists(context)
                && (
                    Directory.GetFiles(context, "*.mp4", SearchOption.TopDirectoryOnly).Length > 0
                    || File.Exists(Path.Combine(context, "youtube-entry.json"))
                );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool MediaExists(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }
        catch (Exception e)
            when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    private static UploadAttemptManifest Read(string path, string attemptId)
    {
        try
        {
            UploadAttemptResult read = UploadAttemptManifestCodec.Read(
                File.ReadAllText(path),
                attemptId
            );
            return read.Succeeded ? read.Manifest : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
