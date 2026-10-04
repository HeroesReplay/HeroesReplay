using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.Core.MediaPolicy;

/// <summary>
/// Writes one observe-only policy snapshot into the attempt manifest.
/// Replay id restarts reuse that snapshot instead of evaluating again.
/// </summary>
public sealed class MediaPolicySnapshot
{
    public string AttemptId { get; init; }
    public ReplayMediaDecision Decision { get; init; }
    public bool Reused { get; init; }
    public bool Persisted { get; init; }

    /// <summary>Always false. The record flag is not evidence that OBS started.</summary>
    public bool RecordingStarted { get; init; }

    /// <summary>
    /// The current settings' reason when a reused decision records but they would not (#204).
    /// The stored decision is kept. Only this launch does not record.
    /// </summary>
    public string RecordingWithheld { get; init; }

    /// <summary>This launch may record: the decision records and the current settings agree.</summary>
    public bool AllowsRecording => Decision?.Record == true && RecordingWithheld == null;
}

public sealed class MediaPolicyAttemptLog
{
    public const string AttemptsDirectoryName = "upload-attempts";

    private readonly UploadOutbox outbox;
    private readonly ILogger logger;

    public MediaPolicyAttemptLog(string attemptsRoot, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(attemptsRoot))
        {
            throw new ArgumentException("An attempt root is required.", nameof(attemptsRoot));
        }

        outbox = new UploadOutbox(attemptsRoot);
        this.logger = logger ?? NullLogger.Instance;
    }

    public static string AttemptsRoot(AppSettings settings)
    {
        string data = settings?.Location?.DataDirectory;
        if (string.IsNullOrWhiteSpace(data))
        {
            throw new InvalidOperationException(
                "Location:DataDirectory is required for the media policy attempt log."
            );
        }

        return Path.Combine(data, AttemptsDirectoryName);
    }

    public Task<MediaPolicySnapshot> RecordPreLaunchAsync(
        LoadedReplay loaded,
        ReplayMediaPolicySettings settings,
        CancellationToken cancellationToken,
        IReadOnlyList<Hero> heroes = null
    )
    {
        return RecordPreLaunchAsync(loaded, settings, DateTime.UtcNow, cancellationToken, heroes);
    }

    public async Task<MediaPolicySnapshot> RecordPreLaunchAsync(
        LoadedReplay loaded,
        ReplayMediaPolicySettings settings,
        DateTime utcNow,
        CancellationToken cancellationToken,
        IReadOnlyList<Hero> heroes = null
    )
    {
        try
        {
            return await RecordPreLaunchCoreAsync(
                    loaded,
                    settings,
                    utcNow,
                    cancellationToken,
                    heroes
                )
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Could not record the pre-launch media policy for replay {ReplayId}.",
                loaded?.ReplayId
            );
            return Unavailable(loaded?.ReplayId, MediaPolicyAttemptIds.For(loaded));
        }
    }

    public async Task<MediaPolicySnapshot> RecordPublicationAsync(
        LoadedReplay loaded,
        MediaPublicationFacts facts,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await RecordPublicationCoreAsync(loaded, facts, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Could not record the media publication decision for replay {ReplayId}.",
                loaded?.ReplayId
            );
            return Unavailable(loaded?.ReplayId, MediaPolicyAttemptIds.For(loaded));
        }
    }

    private async Task<MediaPolicySnapshot> RecordPreLaunchCoreAsync(
        LoadedReplay loaded,
        ReplayMediaPolicySettings settings,
        DateTime utcNow,
        CancellationToken cancellationToken,
        IReadOnlyList<Hero> heroes
    )
    {
        string attemptId = MediaPolicyAttemptIds.For(loaded);
        if (attemptId == null || utcNow.Kind != DateTimeKind.Utc)
        {
            ReplayMediaDecision unstored = Evaluate(
                loaded,
                settings,
                utcNow,
                false,
                false,
                false,
                heroes
            );
            Log(unstored, reused: false);
            return Snapshot(attemptId, unstored, reused: false, persisted: false);
        }

        UploadAttemptResult existing = await outbox
            .LoadAsync(attemptId, cancellationToken)
            .ConfigureAwait(false);
        if (existing.Succeeded && existing.Manifest.Policy != null)
        {
            return WithholdUnlessCurrentRecords(
                Reuse(attemptId, existing.Manifest),
                Evaluate(loaded, settings, utcNow, false, false, false, heroes)
            );
        }

        if (!existing.Succeeded && existing.Reason != UploadAttemptReasons.ManifestMissing)
        {
            logger.LogWarning(
                "Media policy manifest {AttemptId} is {Reason}. It was not reevaluated.",
                attemptId,
                existing.Reason
            );
            return Unavailable(ReplayId(loaded), attemptId);
        }

        Duplicates duplicates = await DuplicatesAsync(
                ReplayId(loaded),
                attemptId,
                loaded?.AlreadyOnYouTube == true,
                cancellationToken
            )
            .ConfigureAwait(false);
        ReplayMediaDecision fresh = Evaluate(
            loaded,
            settings,
            utcNow,
            duplicates.Published,
            duplicates.Scheduled,
            duplicates.InOutbox,
            heroes
        );
        UploadAttemptResult saved = await SaveAsync(
                attemptId,
                fresh,
                utcNow,
                publicationEvaluated: false,
                replaceOpen: false,
                cancellationToken
            )
            .ConfigureAwait(false);
        return FromSave(attemptId, fresh, saved, reusedOnMatch: false);
    }

    private async Task<MediaPolicySnapshot> RecordPublicationCoreAsync(
        LoadedReplay loaded,
        MediaPublicationFacts facts,
        CancellationToken cancellationToken
    )
    {
        string attemptId = MediaPolicyAttemptIds.For(loaded);
        if (attemptId == null)
        {
            return Unavailable(loaded?.ReplayId, null);
        }

        UploadAttemptResult existing = await outbox
            .LoadAsync(attemptId, cancellationToken)
            .ConfigureAwait(false);
        if (!existing.Succeeded || existing.Manifest.Policy == null)
        {
            if (!existing.Succeeded && existing.Reason != UploadAttemptReasons.ManifestMissing)
            {
                logger.LogWarning(
                    "Media policy manifest {AttemptId} is {Reason}. Publication was not reevaluated.",
                    attemptId,
                    existing.Reason
                );
            }

            return Unavailable(ReplayId(loaded), attemptId);
        }

        if (existing.Manifest.Policy.PublicationEvaluated)
        {
            return Reuse(attemptId, existing.Manifest);
        }

        ReplayMediaDecision recorded = MediaPolicyManifest.ToDecision(existing.Manifest);
        if (recorded == null)
        {
            logger.LogWarning(
                "Media policy manifest {AttemptId} could not be read. It was not reevaluated.",
                attemptId
            );
            return Unavailable(ReplayId(loaded), attemptId);
        }

        Duplicates duplicates = await DuplicatesAsync(
                recorded.ReplayId,
                attemptId,
                facts?.AlreadyPublished == true,
                cancellationToken
            )
            .ConfigureAwait(false);
        MediaPublicationFacts combined = Combine(facts, duplicates);
        ReplayMediaDecision final = MediaPolicyPublication.Apply(recorded, combined);
        UploadAttemptResult saved = await SaveAsync(
                attemptId,
                final,
                DateTime.UtcNow,
                publicationEvaluated: true,
                replaceOpen: true,
                cancellationToken
            )
            .ConfigureAwait(false);
        return FromSave(attemptId, final, saved, reusedOnMatch: true);
    }

    /// <summary>
    /// A decision stored under older settings (RecordingMode All, or before the replay expired)
    /// must not record what the current settings refuse (#204).
    /// </summary>
    private MediaPolicySnapshot WithholdUnlessCurrentRecords(
        MediaPolicySnapshot reused,
        ReplayMediaDecision current
    )
    {
        if (reused?.Decision?.Record != true || current == null || current.Record)
        {
            return reused;
        }

        logger.LogInformation(
            "Replay {ReplayId} has a stored decision that records ({StoredReason}), but the current settings do not ({CurrentReason}). This launch does not record.",
            reused.Decision.ReplayId,
            reused.Decision.RecordingReason,
            current.RecordingReason
        );
        return new MediaPolicySnapshot
        {
            AttemptId = reused.AttemptId,
            Decision = reused.Decision,
            Reused = reused.Reused,
            Persisted = reused.Persisted,
            RecordingStarted = false,
            RecordingWithheld = current.RecordingReason,
        };
    }

    private MediaPolicySnapshot Reuse(string attemptId, UploadAttemptManifest manifest)
    {
        ReplayMediaDecision decision = MediaPolicyManifest.ToDecision(manifest);
        if (decision == null)
        {
            logger.LogWarning(
                "Media policy manifest {AttemptId} could not be read. It was not reevaluated.",
                attemptId
            );
            return Unavailable(manifest.ReplayId, attemptId);
        }

        Log(decision, reused: true);
        return Snapshot(attemptId, decision, reused: true, persisted: true);
    }

    private MediaPolicySnapshot FromSave(
        string attemptId,
        ReplayMediaDecision proposed,
        UploadAttemptResult saved,
        bool reusedOnMatch
    )
    {
        if (!saved.Succeeded || saved.Manifest?.Policy == null)
        {
            logger.LogWarning(
                "Could not persist the media policy for {AttemptId}: {Reason}.",
                attemptId,
                saved.Reason
            );
            Log(proposed, reused: false);
            return Snapshot(attemptId, proposed, reused: false, persisted: false);
        }

        ReplayMediaDecision stored = MediaPolicyManifest.ToDecision(saved.Manifest);
        if (stored == null)
        {
            return Unavailable(proposed?.ReplayId, attemptId);
        }

        bool reused = reusedOnMatch && !SameDecision(stored, proposed);
        Log(stored, reused);
        return Snapshot(attemptId, stored, reused, persisted: true);
    }

    private Task<UploadAttemptResult> SaveAsync(
        string attemptId,
        ReplayMediaDecision decision,
        DateTime utcNow,
        bool publicationEvaluated,
        bool replaceOpen,
        CancellationToken cancellationToken
    )
    {
        return outbox.SavePolicyAsync(
            attemptId,
            decision?.ReplayId,
            new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)),
            MediaPolicyManifest.FromDecision(decision, publicationEvaluated),
            replaceOpen,
            cancellationToken
        );
    }

    private static ReplayMediaDecision Evaluate(
        LoadedReplay loaded,
        ReplayMediaPolicySettings settings,
        DateTime utcNow,
        bool alreadyPublished,
        bool alreadyScheduled,
        bool inOutbox,
        IReadOnlyList<Hero> heroes
    )
    {
        return ReplayMediaPolicy.Evaluate(
            ReplayMediaFacts.From(loaded, alreadyPublished, alreadyScheduled, inOutbox, heroes),
            settings,
            utcNow
        );
    }

    private async Task<Duplicates> DuplicatesAsync(
        int? replayId,
        string attemptId,
        bool alreadyPublished,
        CancellationToken cancellationToken
    )
    {
        bool published = alreadyPublished;
        bool inOutbox = false;
        if (replayId is not int id || id <= 0 || !Directory.Exists(outbox.Root))
        {
            return new Duplicates(published, false, inOutbox);
        }

        foreach (string directory in Directory.GetDirectories(outbox.Root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string otherId = Path.GetFileName(directory);
            if (
                string.Equals(otherId, attemptId, StringComparison.Ordinal)
                || !UploadAttemptIds.IsSafe(otherId)
            )
            {
                continue;
            }

            UploadAttemptResult loaded = await outbox
                .LoadAsync(otherId, cancellationToken)
                .ConfigureAwait(false);
            if (!loaded.Succeeded || loaded.Manifest.ReplayId != id)
            {
                continue;
            }

            if (loaded.Manifest.State == UploadAttemptState.Uploaded)
            {
                published = true;
            }
            else
            {
                inOutbox = true;
            }
        }

        return new Duplicates(published, false, inOutbox);
    }

    private static MediaPublicationFacts Combine(MediaPublicationFacts facts, Duplicates duplicates)
    {
        facts ??= new MediaPublicationFacts();
        return new MediaPublicationFacts
        {
            Outcome = facts.Outcome,
            MatchClockSeen = facts.MatchClockSeen,
            HudSamples = facts.HudSamples,
            RecordedFor = facts.RecordedFor,
            Recording = facts.Recording,
            AlreadyPublished = facts.AlreadyPublished || duplicates.Published,
            AlreadyScheduled = facts.AlreadyScheduled,
            InOutbox = facts.InOutbox || duplicates.InOutbox,
        };
    }

    private void Log(ReplayMediaDecision decision, bool reused)
    {
        if (decision == null)
        {
            return;
        }

        logger.LogInformation(
            "Replay {ReplayId} media policy {PolicyVersion} configuration {ConfigurationVersion} records {Record} ({RecordingReason}), publication {PublicationCandidate} ({PublicationReason}), priority {Priority}, score {Score}, expires {ExpiresAtUtc}, reused {Reused}. This decision did not start recording.",
            decision.ReplayId,
            decision.PolicyVersion,
            decision.ConfigurationVersion,
            decision.Record,
            decision.RecordingReason,
            decision.PublicationCandidate,
            decision.PublicationReason,
            decision.Priority,
            decision.Score.Total,
            decision.CandidateExpiresAtUtc,
            reused
        );
    }

    private static MediaPolicySnapshot Snapshot(
        string attemptId,
        ReplayMediaDecision decision,
        bool reused,
        bool persisted
    )
    {
        return new MediaPolicySnapshot
        {
            AttemptId = attemptId,
            Decision = decision,
            Reused = reused,
            Persisted = persisted,
            RecordingStarted = false,
        };
    }

    private static MediaPolicySnapshot Unavailable(int? replayId, string attemptId)
    {
        return Snapshot(
            attemptId,
            new ReplayMediaDecision
            {
                PolicyVersion = ReplayMediaPolicy.PolicyVersion,
                ReplayId = replayId is int id && id > 0 ? id : null,
                Record = false,
                RecordingReason = ReplayMediaReason.MissingInput,
                PublicationCandidate = false,
                PublicationReason = ReplayMediaReason.MissingInput,
                Priority = ReplayMediaPriority.Ordinary,
                NotableEvents = Array.Empty<TeamKillClip>(),
                ConfigurationErrors = Array.Empty<string>(),
                RecordingMode = ReplayRecordingMode.Disabled,
                PublicationMode = ReplayPublicationMode.Disabled,
            },
            reused: false,
            persisted: false
        );
    }

    private static bool SameDecision(ReplayMediaDecision left, ReplayMediaDecision right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        return left.Record == right.Record
            && left.PublicationCandidate == right.PublicationCandidate
            && left.PolicyVersion == right.PolicyVersion
            && left.RecordingReason == right.RecordingReason
            && left.PublicationReason == right.PublicationReason
            && left.Score.Total == right.Score.Total;
    }

    private static int? ReplayId(LoadedReplay loaded)
    {
        return loaded?.ReplayId is int id && id > 0 ? id : null;
    }

    private readonly record struct Duplicates(bool Published, bool Scheduled, bool InOutbox);
}

public static class MediaPolicyAttemptIds
{
    public static string For(LoadedReplay loaded)
    {
        if (loaded?.ReplayId is int replayId && replayId > 0)
        {
            return "replay-" + replayId.ToString(CultureInfo.InvariantCulture);
        }

        if (loaded?.HeroesProfileReplay?.Id is int profileId && profileId > 0)
        {
            return "replay-" + profileId.ToString(CultureInfo.InvariantCulture);
        }

        string token = Sanitize(loaded?.FileInfo?.Name);
        return token == null ? null : "file-" + token;
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string stem = Path.GetFileNameWithoutExtension(name);
        var chars = new char[stem.Length];
        int count = 0;
        foreach (char character in stem)
        {
            if (char.IsLetterOrDigit(character) || character == '-' || character == '_')
            {
                chars[count++] = character;
            }
        }

        if (count == 0)
        {
            return null;
        }

        string token = new string(chars, 0, count);
        int max = UploadAttemptIds.MaxLength - "file-".Length;
        if (token.Length > max)
        {
            token = token.Substring(0, max);
        }

        string attemptId = "file-" + token;
        return UploadAttemptIds.IsSafe(attemptId) ? token : null;
    }
}
