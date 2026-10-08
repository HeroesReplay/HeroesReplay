using System;
using System.Collections.Generic;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.HeroesProfile;

namespace HeroesReplay.Core.MediaPolicy;

/// <summary>
/// Deterministic recording and publication-candidate decision.
/// Expiration is anchored to the game date. The supplied clock only decides whether that
/// instant has passed. A local clock is rejected. Map and hero novelty are not scored;
/// those need publication history.
/// </summary>
public static class ReplayMediaPolicy
{
    public const string PolicyVersion = "1";
    public const int MaxCandidateAgeDays = 3660;
    public const int RequestedScore = 400_000;
    public const int NotableScore = 300_000;
    public const int HighSkillScore = 200_000;
    public const int OrdinaryScore = 100_000;
    public const long RecencyHorizonMinutes = 20_160;
    public const long ScoreComponentCap = 20_000;
    public const long PentakillScore = 100;
    public const long TeamWipeScore = 60;

    public static IReadOnlyList<string> Validate(ReplayMediaPolicySettings settings)
    {
        if (settings == null)
        {
            return new[] { ReplayMediaConfigurationError.SettingsMissing };
        }

        var errors = new List<string>();
        if (!Enum.IsDefined(settings.RecordingMode))
        {
            errors.Add(ReplayMediaConfigurationError.RecordingModeInvalid);
        }

        if (!Enum.IsDefined(settings.PublicationMode))
        {
            errors.Add(ReplayMediaConfigurationError.PublicationModeInvalid);
        }

        if (string.IsNullOrWhiteSpace(settings.Version))
        {
            errors.Add(ReplayMediaConfigurationError.ConfigurationVersionMissing);
        }

        if (
            Enum.IsDefined(settings.RecordingMode)
            && Enum.IsDefined(settings.PublicationMode)
            && settings.RecordingMode == ReplayRecordingMode.Disabled
            && settings.PublicationMode != ReplayPublicationMode.Disabled
        )
        {
            errors.Add(ReplayMediaConfigurationError.RecordingDisabledWhilePublishing);
        }

        if (settings.RequireCurrentPatch)
        {
            string floor =
                settings.MinimumGameVersion == null ? null : settings.MinimumGameVersion.Trim();
            if (string.IsNullOrEmpty(floor))
            {
                errors.Add(ReplayMediaConfigurationError.MinimumGameVersionMissing);
            }
            else if (!IsDottedNumeric(floor))
            {
                errors.Add(ReplayMediaConfigurationError.MinimumGameVersionInvalid);
            }
        }

        AddAge(
            errors,
            settings.OrdinaryCandidateMaxAge,
            ReplayMediaConfigurationError.OrdinaryMaxAgeNegative,
            ReplayMediaConfigurationError.OrdinaryMaxAgeTooLarge
        );
        AddAge(
            errors,
            settings.HighSkillCandidateMaxAge,
            ReplayMediaConfigurationError.HighSkillMaxAgeNegative,
            ReplayMediaConfigurationError.HighSkillMaxAgeTooLarge
        );
        AddAge(
            errors,
            settings.NotableCandidateMaxAge,
            ReplayMediaConfigurationError.NotableMaxAgeNegative,
            ReplayMediaConfigurationError.NotableMaxAgeTooLarge
        );
        AddAge(
            errors,
            settings.RequestedCandidateMaxAge,
            ReplayMediaConfigurationError.RequestedMaxAgeNegative,
            ReplayMediaConfigurationError.RequestedMaxAgeTooLarge
        );
        AddAge(
            errors,
            settings.MinimumPublicInterval,
            ReplayMediaConfigurationError.MinimumPublicIntervalNegative,
            ReplayMediaConfigurationError.MinimumPublicIntervalTooLarge
        );
        AddAge(
            errors,
            settings.MaxPublishAhead,
            ReplayMediaConfigurationError.MaxPublishAheadNegative,
            ReplayMediaConfigurationError.MaxPublishAheadTooLarge
        );
        AddAge(
            errors,
            settings.MapCooldown,
            ReplayMediaConfigurationError.MapCooldownNegative,
            ReplayMediaConfigurationError.MapCooldownTooLarge
        );
        AddAge(
            errors,
            settings.RankCooldown,
            ReplayMediaConfigurationError.RankCooldownNegative,
            ReplayMediaConfigurationError.RankCooldownTooLarge
        );
        AddAge(
            errors,
            settings.FeaturedHeroCooldown,
            ReplayMediaConfigurationError.FeaturedHeroCooldownNegative,
            ReplayMediaConfigurationError.FeaturedHeroCooldownTooLarge
        );
        AddCount(
            errors,
            settings.MaxSharedHeroes,
            ReplayMediaConfigurationError.SharedHeroesNegative
        );
        AddCount(
            errors,
            settings.MaxPublicPerDay,
            ReplayMediaConfigurationError.PublicDayCapNegative
        );
        AddCount(
            errors,
            settings.MaxPublicPerWeek,
            ReplayMediaConfigurationError.PublicWeekCapNegative
        );
        AddCount(
            errors,
            settings.ReservedRequestSlotsPerDay,
            ReplayMediaConfigurationError.ReservedRequestSlotsNegative
        );
        AddCount(
            errors,
            settings.MaxInsertsPerQuotaDay,
            ReplayMediaConfigurationError.InsertQuotaNegative
        );

        if (settings.MinimumHighSkillMmr is int mmrFloor && mmrFloor < 0)
        {
            errors.Add(ReplayMediaConfigurationError.HighSkillMmrNegative);
        }

        if (
            !string.IsNullOrWhiteSpace(settings.MinimumHighSkillRank)
            && ReplayMediaRanks.Index(settings.MinimumHighSkillRank) == null
        )
        {
            errors.Add(ReplayMediaConfigurationError.HighSkillRankInvalid);
        }

        return errors.Count == 0 ? Array.Empty<string>() : errors.ToArray();
    }

    public static ReplayMediaDecision Evaluate(
        ReplayMediaPolicyInput input,
        ReplayMediaPolicySettings settings,
        DateTime utcNow
    )
    {
        IReadOnlyList<string> errors = Validate(settings);
        if (errors.Count > 0)
        {
            return Finish(
                settings,
                input,
                utcNow,
                false,
                ReplayMediaReason.ConfigurationInvalid,
                false,
                ReplayMediaReason.ConfigurationInvalid,
                ReplayMediaPriority.Ordinary,
                default,
                Array.Empty<TeamKillClip>(),
                null,
                errors
            );
        }

        if (utcNow.Kind == DateTimeKind.Local)
        {
            return Finish(
                settings,
                input,
                utcNow,
                false,
                ReplayMediaReason.ClockNotUtc,
                false,
                ReplayMediaReason.ClockNotUtc,
                ReplayMediaPriority.Ordinary,
                default,
                Array.Empty<TeamKillClip>(),
                null,
                Array.Empty<string>()
            );
        }

        DateTime now =
            utcNow.Kind == DateTimeKind.Utc
                ? utcNow
                : DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

        if (input == null)
        {
            return Reject(settings, null, now, ReplayMediaReason.MissingInput);
        }

        if (input.GameDateUtc is DateTime dated && dated.Kind == DateTimeKind.Local)
        {
            return Reject(settings, input, now, ReplayMediaReason.GameDateNotUtc);
        }

        if (input.AverageMmr is double mmr && IsMalformedMmr(mmr))
        {
            return Reject(settings, input, now, ReplayMediaReason.MalformedInput);
        }

        if (input.ReplayId is not int replayId || replayId <= 0)
        {
            return Reject(settings, input, now, ReplayMediaReason.MissingIdentity);
        }

        TeamKillClip[] events = ReplayMediaEvidence.Accepted(input.NotableEvents);
        ReplayMediaPriority priority = Classify(input, settings, events.Length > 0);
        DateTime? gameDate = NormalizeGameDate(input.GameDateUtc);
        if (!TryExpiry(gameDate, Window(priority, settings), out DateTime? expires))
        {
            return Reject(settings, input, now, ReplayMediaReason.MalformedInput);
        }

        ReplayMediaScore score = ScoreOf(input, settings, events, priority, gameDate, now);
        if (input.AlreadyPublished)
        {
            return Copy(
                settings,
                input,
                now,
                false,
                ReplayMediaReason.AlreadyPublished,
                priority,
                score,
                events,
                expires
            );
        }

        if (input.AlreadyScheduled)
        {
            return Copy(
                settings,
                input,
                now,
                false,
                ReplayMediaReason.AlreadyScheduled,
                priority,
                score,
                events,
                expires
            );
        }

        if (input.InOutbox)
        {
            return Copy(
                settings,
                input,
                now,
                false,
                ReplayMediaReason.InOutbox,
                priority,
                score,
                events,
                expires
            );
        }

        if (input.ViewerRequested && !input.RecordAndUpload)
        {
            return Copy(
                settings,
                input,
                now,
                false,
                ReplayMediaReason.SpectateOnly,
                priority,
                score,
                events,
                expires
            );
        }

        bool onPatch =
            !settings.RequireCurrentPatch
            || GameVersionOrder.IsAtLeast(
                TrimToNull(input.GameVersion),
                settings.MinimumGameVersion.Trim()
            );
        bool patchAllowed =
            onPatch
            || (
                priority == ReplayMediaPriority.Requested && settings.RequestsBypassPatchRequirement
            );
        bool expired = IsPast(expires, now);
        bool openPool =
            settings.PublicationMode == ReplayPublicationMode.Curated
            || settings.PublicationMode == ReplayPublicationMode.AllEligible;
        Gate recording = RecordingGate(
            settings,
            priority,
            patchAllowed,
            gameDate.HasValue,
            expired,
            openPool
        );
        Gate publication = PublicationGate(
            settings,
            input,
            priority,
            patchAllowed,
            gameDate.HasValue,
            expired
        );
        return Finish(
            settings,
            input,
            now,
            recording.Allow,
            recording.Reason,
            publication.Allow,
            publication.Reason,
            priority,
            score,
            events,
            expires,
            Array.Empty<string>()
        );
    }

    /// <summary>
    /// True when the replay is past its media window: the game date plus the candidate max age
    /// for its priority has passed, the rule that makes <see cref="Evaluate"/> refuse it as
    /// <see cref="ReplayMediaReason.Expired"/>. False inside the window, and when the window
    /// cannot be judged (no game date, invalid settings).
    /// </summary>
    public static bool IsPastWindow(
        ReplayMediaPolicyInput input,
        ReplayMediaPolicySettings settings,
        DateTime utcNow
    )
    {
        ReplayMediaDecision decision = Evaluate(input, settings, utcNow);
        return IsPast(decision.CandidateExpiresAtUtc, decision.EvaluatedAtUtc);
    }

    private static bool IsPast(DateTime? expires, DateTime now) =>
        expires.HasValue && now > expires.Value;

    private static ReplayMediaDecision Reject(
        ReplayMediaPolicySettings settings,
        ReplayMediaPolicyInput input,
        DateTime now,
        string reason
    )
    {
        return Finish(
            settings,
            input,
            now,
            false,
            reason,
            false,
            reason,
            ReplayMediaPriority.Ordinary,
            default,
            Array.Empty<TeamKillClip>(),
            null,
            Array.Empty<string>()
        );
    }

    private static ReplayMediaDecision Copy(
        ReplayMediaPolicySettings settings,
        ReplayMediaPolicyInput input,
        DateTime now,
        bool allow,
        string reason,
        ReplayMediaPriority priority,
        ReplayMediaScore score,
        TeamKillClip[] events,
        DateTime? expires
    )
    {
        return Finish(
            settings,
            input,
            now,
            allow,
            reason,
            allow,
            reason,
            priority,
            score,
            events,
            expires,
            Array.Empty<string>()
        );
    }

    private static Gate RecordingGate(
        ReplayMediaPolicySettings settings,
        ReplayMediaPriority priority,
        bool patchAllowed,
        bool hasGameDate,
        bool expired,
        bool openPool
    )
    {
        if (settings.RecordingMode == ReplayRecordingMode.Disabled)
        {
            return new Gate(false, ReplayMediaReason.RecordingDisabled);
        }

        if (!patchAllowed && settings.RecordingMode != ReplayRecordingMode.All)
        {
            return new Gate(false, ReplayMediaReason.OffPatch);
        }

        if (!ModeRecords(settings.RecordingMode, priority, openPool))
        {
            string reason =
                settings.RecordingMode == ReplayRecordingMode.RequestedOnly
                    ? ReplayMediaReason.NotRequested
                    : ReplayMediaReason.NotSelected;
            return new Gate(false, reason);
        }

        if (!hasGameDate && settings.RecordingMode != ReplayRecordingMode.All)
        {
            return new Gate(false, ReplayMediaReason.MissingGameDate);
        }

        if (expired && settings.RecordingMode != ReplayRecordingMode.All)
        {
            return new Gate(false, ReplayMediaReason.Expired);
        }

        if (settings.RecordingMode == ReplayRecordingMode.All)
        {
            return new Gate(true, ReplayMediaReason.RecordedAll);
        }

        return new Gate(true, RecordedReason(priority));
    }

    private static Gate PublicationGate(
        ReplayMediaPolicySettings settings,
        ReplayMediaPolicyInput input,
        ReplayMediaPriority priority,
        bool patchAllowed,
        bool hasGameDate,
        bool expired
    )
    {
        if (settings.PublicationMode == ReplayPublicationMode.Disabled)
        {
            return new Gate(false, ReplayMediaReason.PublicationDisabled);
        }

        if (!patchAllowed)
        {
            return new Gate(false, ReplayMediaReason.OffPatch);
        }

        if (
            settings.PublicationMode == ReplayPublicationMode.RequestedOnly
            && priority != ReplayMediaPriority.Requested
        )
        {
            return new Gate(false, ReplayMediaReason.NotRequested);
        }

        if (!hasGameDate)
        {
            return new Gate(false, ReplayMediaReason.MissingGameDate);
        }

        if (expired)
        {
            return new Gate(false, ReplayMediaReason.Expired);
        }

        if (input.Completion == null)
        {
            return new Gate(false, ReplayMediaReason.AwaitingCompletion);
        }

        if (!input.Completion.IsVerifiedComplete)
        {
            return new Gate(false, ReplayMediaReason.Incomplete);
        }

        if (input.Media == null)
        {
            return new Gate(false, ReplayMediaReason.AwaitingMedia);
        }

        if (!input.Media.IsFinalized)
        {
            return new Gate(false, ReplayMediaReason.MediaNotFinalized);
        }

        if (!input.Media.IsCorrelated)
        {
            return new Gate(false, ReplayMediaReason.MediaNotCorrelated);
        }

        if (priority == ReplayMediaPriority.Requested)
        {
            return new Gate(true, ReplayMediaReason.EligibleRequested);
        }

        if (settings.PublicationMode == ReplayPublicationMode.AllEligible)
        {
            return new Gate(true, ReplayMediaReason.EligibleAll);
        }

        return new Gate(true, ReplayMediaReason.EligibleCurated);
    }

    private static bool ModeRecords(
        ReplayRecordingMode mode,
        ReplayMediaPriority priority,
        bool openPool
    )
    {
        if (mode == ReplayRecordingMode.All)
        {
            return true;
        }

        if (mode == ReplayRecordingMode.RequestedOnly)
        {
            return priority == ReplayMediaPriority.Requested;
        }

        if (mode == ReplayRecordingMode.Selected)
        {
            return priority == ReplayMediaPriority.Requested
                || priority == ReplayMediaPriority.Notable
                || openPool;
        }

        return false;
    }

    private static string RecordedReason(ReplayMediaPriority priority)
    {
        switch (priority)
        {
            case ReplayMediaPriority.Requested:
                return ReplayMediaReason.RecordedRequested;
            case ReplayMediaPriority.Notable:
                return ReplayMediaReason.RecordedNotable;
            case ReplayMediaPriority.HighSkill:
                return ReplayMediaReason.RecordedHighSkill;
            default:
                return ReplayMediaReason.RecordedOrdinary;
        }
    }

    private static ReplayMediaPriority Classify(
        ReplayMediaPolicyInput input,
        ReplayMediaPolicySettings settings,
        bool notable
    )
    {
        if (input.RecordAndUpload)
        {
            return ReplayMediaPriority.Requested;
        }

        if (notable)
        {
            return ReplayMediaPriority.Notable;
        }

        if (IsHighSkill(input, settings))
        {
            return ReplayMediaPriority.HighSkill;
        }

        return ReplayMediaPriority.Ordinary;
    }

    private static bool IsHighSkill(
        ReplayMediaPolicyInput input,
        ReplayMediaPolicySettings settings
    )
    {
        bool rankConfigured = !string.IsNullOrWhiteSpace(settings.MinimumHighSkillRank);
        bool mmrConfigured = settings.MinimumHighSkillMmr != null;
        if (!rankConfigured && !mmrConfigured)
        {
            return false;
        }

        if (rankConfigured && RankSteps(input.Rank, settings.MinimumHighSkillRank) >= 0)
        {
            return true;
        }

        return mmrConfigured
            && input.AverageMmr is double mmr
            && mmr >= settings.MinimumHighSkillMmr.Value;
    }

    private static int RankSteps(string actualRank, string floorRank)
    {
        int? actual = ReplayMediaRanks.Index(actualRank);
        int? floor = ReplayMediaRanks.Index(floorRank);
        if (actual == null || floor == null)
        {
            return -1;
        }

        return actual.Value - floor.Value;
    }

    private static TimeSpan Window(ReplayMediaPriority priority, ReplayMediaPolicySettings settings)
    {
        switch (priority)
        {
            case ReplayMediaPriority.Requested:
                return settings.RequestedCandidateMaxAge;
            case ReplayMediaPriority.Notable:
                return settings.NotableCandidateMaxAge;
            case ReplayMediaPriority.HighSkill:
                return settings.HighSkillCandidateMaxAge;
            default:
                return settings.OrdinaryCandidateMaxAge;
        }
    }

    private static bool TryExpiry(DateTime? gameDate, TimeSpan window, out DateTime? expires)
    {
        expires = null;
        if (gameDate == null)
        {
            return true;
        }

        DateTime start = gameDate.Value;
        if (window > TimeSpan.Zero && start > DateTime.MaxValue - window)
        {
            return false;
        }

        DateTime sum = start + window;
        expires = sum.Kind == DateTimeKind.Utc ? sum : DateTime.SpecifyKind(sum, DateTimeKind.Utc);
        return true;
    }

    private static ReplayMediaScore ScoreOf(
        ReplayMediaPolicyInput input,
        ReplayMediaPolicySettings settings,
        IReadOnlyList<TeamKillClip> events,
        ReplayMediaPriority priority,
        DateTime? gameDate,
        DateTime now
    )
    {
        int weight = Weight(priority);
        long recency = 0;
        long tieDate = 0;
        if (gameDate != null)
        {
            tieDate = gameDate.Value.Ticks;
            long ageTicks = now.Ticks - gameDate.Value.Ticks;
            long ageMinutes = ageTicks > 0 ? ageTicks / TimeSpan.TicksPerMinute : 0;
            recency = ageMinutes >= RecencyHorizonMinutes ? 0 : RecencyHorizonMinutes - ageMinutes;
        }

        long notable = 0;
        foreach (TeamKillClip clip in events)
        {
            if (clip.Kind == TeamKillClips.PentakillKind)
            {
                notable += PentakillScore;
                // A pentakill that killed the whole team scores more; it is still one event (#369).
                if (clip.WipedTeam)
                {
                    notable += TeamWipeScore;
                }
            }
        }

        if (notable > ScoreComponentCap)
        {
            notable = ScoreComponentCap;
        }

        long skill = SkillPoints(input, settings);
        if (skill > ScoreComponentCap)
        {
            skill = ScoreComponentCap;
        }

        int replayId = input.ReplayId ?? 0;
        return new ReplayMediaScore(
            weight,
            recency,
            notable,
            skill,
            weight + recency + notable + skill,
            replayId,
            tieDate
        );
    }

    private static int Weight(ReplayMediaPriority priority)
    {
        switch (priority)
        {
            case ReplayMediaPriority.Requested:
                return RequestedScore;
            case ReplayMediaPriority.Notable:
                return NotableScore;
            case ReplayMediaPriority.HighSkill:
                return HighSkillScore;
            default:
                return OrdinaryScore;
        }
    }

    private static long SkillPoints(
        ReplayMediaPolicyInput input,
        ReplayMediaPolicySettings settings
    )
    {
        long points = 0;
        int steps = RankSteps(input.Rank, settings.MinimumHighSkillRank);
        if (steps > 0)
        {
            points += steps * 100L;
        }

        if (
            settings.MinimumHighSkillMmr is int floor
            && input.AverageMmr is double mmr
            && mmr > floor
        )
        {
            points += (long)Math.Floor(mmr - floor);
        }

        return points;
    }

    private static bool IsMalformedMmr(double mmr)
    {
        return double.IsNaN(mmr) || double.IsInfinity(mmr) || mmr < 0;
    }

    private static DateTime? NormalizeGameDate(DateTime? gameDate)
    {
        if (gameDate is not DateTime value || value.Kind == DateTimeKind.Local)
        {
            return null;
        }

        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private static string TrimToNull(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static double? SafeMmr(double? mmr)
    {
        if (mmr is not double value || IsMalformedMmr(value))
        {
            return null;
        }

        return value;
    }

    private static void AddAge(List<string> errors, TimeSpan age, string negative, string tooLarge)
    {
        if (age < TimeSpan.Zero)
        {
            errors.Add(negative);
            return;
        }

        if (age > TimeSpan.FromDays(MaxCandidateAgeDays))
        {
            errors.Add(tooLarge);
        }
    }

    private static void AddCount(List<string> errors, int value, string negative)
    {
        if (value < 0)
        {
            errors.Add(negative);
        }
    }

    private static bool IsDottedNumeric(string version)
    {
        string[] parts = version.Split('.');
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (string part in parts)
        {
            if (part.Length == 0)
            {
                return false;
            }

            foreach (char character in part)
            {
                if (!char.IsDigit(character))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static ReplayMediaDecision Finish(
        ReplayMediaPolicySettings settings,
        ReplayMediaPolicyInput input,
        DateTime evaluatedAt,
        bool record,
        string recordingReason,
        bool publicationCandidate,
        string publicationReason,
        ReplayMediaPriority priority,
        ReplayMediaScore score,
        IReadOnlyList<TeamKillClip> events,
        DateTime? expiresAt,
        IReadOnlyList<string> errors
    )
    {
        ReplayRecordingMode recordingMode = ReplayRecordingMode.Disabled;
        ReplayPublicationMode publicationMode = ReplayPublicationMode.Disabled;
        string configurationVersion = null;
        if (settings != null)
        {
            if (Enum.IsDefined(settings.RecordingMode))
            {
                recordingMode = settings.RecordingMode;
            }

            if (Enum.IsDefined(settings.PublicationMode))
            {
                publicationMode = settings.PublicationMode;
            }

            if (!string.IsNullOrWhiteSpace(settings.Version))
            {
                configurationVersion = settings.Version.Trim();
            }
        }

        return new ReplayMediaDecision
        {
            PolicyVersion = PolicyVersion,
            ConfigurationVersion = configurationVersion,
            EvaluatedAtUtc = evaluatedAt,
            Record = record,
            RecordingReason = recordingReason,
            PublicationCandidate = publicationCandidate,
            PublicationReason = publicationReason,
            Priority = priority,
            Score = score,
            NotableEvents = events ?? Array.Empty<TeamKillClip>(),
            CandidateExpiresAtUtc = expiresAt,
            RecordingMode = recordingMode,
            PublicationMode = publicationMode,
            SchedulerCurationRequired =
                publicationCandidate && publicationMode == ReplayPublicationMode.Curated,
            ConfigurationErrors = errors ?? Array.Empty<string>(),
            ReplayId = input?.ReplayId is int id && id > 0 ? id : null,
            GameDateUtc = input == null ? null : NormalizeGameDate(input.GameDateUtc),
            GameVersion = input == null ? null : TrimToNull(input.GameVersion),
            Map = input == null ? null : TrimToNull(input.Map),
            GameMode = input == null ? null : TrimToNull(input.GameMode),
            Rank = input == null ? null : TrimToNull(input.Rank),
            AverageMmr = input == null ? null : SafeMmr(input.AverageMmr),
            FocusHero = input == null ? null : TrimToNull(input.FocusHero),
        };
    }

    private readonly record struct Gate(bool Allow, string Reason);
}
