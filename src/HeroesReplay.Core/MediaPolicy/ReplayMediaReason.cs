namespace HeroesReplay.Core.MediaPolicy;

/// <summary>Stable decision codes. Persisted values, not display text.</summary>
public static class ReplayMediaReason
{
    public const string ConfigurationInvalid = "configuration-invalid";
    public const string ClockNotUtc = "clock-not-utc";
    public const string MissingInput = "missing-input";
    public const string MalformedInput = "malformed-input";
    public const string GameDateNotUtc = "game-date-not-utc";
    public const string MissingIdentity = "missing-identity";
    public const string AlreadyPublished = "already-published";
    public const string AlreadyScheduled = "already-scheduled";
    public const string InOutbox = "in-outbox";
    public const string SpectateOnly = "spectate-only";
    public const string RecordingDisabled = "recording-disabled";
    public const string PublicationDisabled = "publication-disabled";
    public const string OffPatch = "off-patch";
    public const string NotRequested = "not-requested";
    public const string NotSelected = "not-selected";
    public const string MissingGameDate = "missing-game-date";
    public const string Expired = "expired";
    public const string AwaitingCompletion = "awaiting-completion";
    public const string Incomplete = "incomplete";
    public const string AwaitingMedia = "awaiting-media";
    public const string MediaNotFinalized = "media-not-finalized";
    public const string MediaNotCorrelated = "media-not-correlated";
    public const string RecordedAll = "recorded-all";
    public const string RecordedRequested = "recorded-requested";
    public const string RecordedNotable = "recorded-notable";
    public const string RecordedHighSkill = "recorded-high-skill";
    public const string RecordedOrdinary = "recorded-ordinary";
    public const string EligibleRequested = "eligible-requested";
    public const string EligibleCurated = "eligible-curated";
    public const string EligibleAll = "eligible-all";
}

public static class ReplayMediaConfigurationError
{
    public const string SettingsMissing = "settings-missing";
    public const string RecordingModeInvalid = "recording-mode-invalid";
    public const string PublicationModeInvalid = "publication-mode-invalid";
    public const string ConfigurationVersionMissing = "configuration-version-missing";
    public const string MinimumGameVersionMissing = "minimum-game-version-missing";
    public const string MinimumGameVersionInvalid = "minimum-game-version-invalid";
    public const string RecordingDisabledWhilePublishing = "recording-disabled-while-publishing";
    public const string OrdinaryMaxAgeNegative = "ordinary-max-age-negative";
    public const string HighSkillMaxAgeNegative = "high-skill-max-age-negative";
    public const string NotableMaxAgeNegative = "notable-max-age-negative";
    public const string RequestedMaxAgeNegative = "requested-max-age-negative";
    public const string OrdinaryMaxAgeTooLarge = "ordinary-max-age-too-large";
    public const string HighSkillMaxAgeTooLarge = "high-skill-max-age-too-large";
    public const string NotableMaxAgeTooLarge = "notable-max-age-too-large";
    public const string RequestedMaxAgeTooLarge = "requested-max-age-too-large";
    public const string HighSkillMmrNegative = "high-skill-mmr-negative";
    public const string HighSkillRankInvalid = "high-skill-rank-invalid";
    public const string PublicDayCapNegative = "public-day-cap-negative";
    public const string PublicWeekCapNegative = "public-week-cap-negative";
    public const string MinimumPublicIntervalNegative = "minimum-public-interval-negative";
    public const string MinimumPublicIntervalTooLarge = "minimum-public-interval-too-large";
    public const string MaxPublishAheadNegative = "max-publish-ahead-negative";
    public const string MaxPublishAheadTooLarge = "max-publish-ahead-too-large";
    public const string MapCooldownNegative = "map-cooldown-negative";
    public const string MapCooldownTooLarge = "map-cooldown-too-large";
    public const string FeaturedHeroCooldownNegative = "featured-hero-cooldown-negative";
    public const string FeaturedHeroCooldownTooLarge = "featured-hero-cooldown-too-large";
    public const string RankCooldownNegative = "rank-cooldown-negative";
    public const string RankCooldownTooLarge = "rank-cooldown-too-large";
    public const string SharedHeroesNegative = "shared-heroes-negative";
    public const string ReservedRequestSlotsNegative = "reserved-request-slots-negative";
    public const string InsertQuotaNegative = "insert-quota-negative";
}
