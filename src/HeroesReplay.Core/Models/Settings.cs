using Heroes.ReplayParser;

using HeroesReplay.Core.Processes;

using System;
using System.Collections.Generic;
using System.IO;

namespace System.Runtime.CompilerServices
{
    public class IsExternalInit { }
}

namespace HeroesReplay.Core.Shared
{

    public record FeatureToggleSettings
    {
        public bool EnableTwitchClips { get; init; }
        public bool EnableMMR { get; init; }
        public bool ForceLaunch { get; init; }
        public bool SaveCaptureFailureCondition { get; init; }
        public bool DefaultInterface { get; init; }
    }

    public record ProcessSettings
    {
        public string BattlenetProcessName { get; init; }
        public string HeroesOfTheStormProcessName { get; init; }
    }

    public record StormReplaySettings
    {
        public string InfoFileName { get; init; }
        public string StormReplayFileWildCard { get; init; }
        public string StormReplayFileExtension { get; init; }
    }

    public record HeroesProfileApiSettings
    {
        public Uri HeroesProfileBaseUri { get; init; }
        public string HeroesProfileApiKey { get; init; }
    }

    public record HotsApiSettings
    {
        public string AwsAccessKey { get; init; }
        public string AwsSecretKey { get; init; }
        public int ReplayIdUnset { get; init; }
        public int MinReplayId { get; init; }
        public int ReplayIdBaseline { get; init; }
        public Uri HotsApiBaseUri { get; init; }
        public string CachedFileNameSplitter { get; init; }
        public IEnumerable<string> HotsApiGameTypes { get; init; }
    }

    public record TwitchApiSettings
    {
        public string TwitchAccessToken { get; init; }
        public string TwitchClientId { get; init; }
    }

    public record SpectateWeightSettings
    {
        public float Roaming { get; init; }
        public float KillingMinions { get; init; }
        public float NearCaptureBeacon { get; init; }
        public float MercClear { get; init; }
        public float TauntingEmote { get; init; }
        public float TauntingDance { get; init; }
        public float TauntingBStep { get; init; }
        public float DestroyStructure { get; init; }
        public float CampCapture { get; init; }
        public float BossCapture { get; init; }
        public float MapObjective { get; init; }
        public float NearEnemyCore { get; init; }
        public float NearEnemyHero { get; init; }
        public float PlayerDeath { get; init; }
        public float PlayerKill { get; init; }
    }

    public record ParseOptionsSettings
    {
        public bool ShouldParseEvents { get; init; }
        public bool ShouldParseMouseEvents { get; init; }
        public bool ShouldParseMessageEvents { get; init; }
        public bool ShouldParseStatistics { get; init; }
        public bool ShouldParseUnits { get; init; }
        public bool ShouldParseDetailedBattleLobby { get; init; }
    }

    public record MapSettings
    {
        public IEnumerable<string> CarriedObjectives { get; init; }
        public IEnumerable<string> ARAM { get; init; }
    }

    public record UnitSettings
    {
        public IEnumerable<string> IgnoreNames { get; init; }
        public IEnumerable<string> CoreNames { get; init; }
        public IEnumerable<string> BossNames { get; init; }
        public IEnumerable<string> CampNames { get; init; }
        public IEnumerable<string> MapObjectiveNames { get; init; }
        public IEnumerable<string> CaptureNames { get; init; }
    }

    public record OCRSettings
    {
        public IEnumerable<string> HomeScreenText { get; init; }
        public IEnumerable<string> LoadingScreenText { get; init; }
        public string TimerSeperator { get; init; }
        public string TimerNegativePrefix { get; init; }
        public int TimerHours { get; init; }
        public int TimerMinutes { get; init; }
        public string TimeSpanFormatHours { get; init; }
        public string TimeSpanFormatMatchStart { get; init; }
        public string TimeSpanFormatMinutes { get; init; }
    }

    public record LocationSettings
    {
        public string BattlenetPath { get; init; }
        public string GameInstallPath { get; init; }
        public string ReplaySourcePath { get; init; }
    }

    public record CaptureSettings
    {
        public CaptureMethod CaptureMethod { get; init; }
        public string CaptureConditionFailurePath { get; init; }
    }

    public record SpectateSettings
    {
        public int GameLoopsOffset { get; init; }
        public int GameLoopsPerSecond { get; init; }
        public Version MinVersionSupported { get; init; }
        public int MaxDistanceToCore { get; init; }
        public int MaxDistanceToEnemy { get; init; }
        public int MaxDistanceToObjective { get; init; }
        public int MaxDistanceToOwnerChange { get; init; }
        public string StormInterface { get; init; }
        public TimeSpan EndScreenTime { get; init; }
        public IEnumerable<int> TalentLevels { get; init; }
    }

    public record AbilityBuild
    {
        public int AbilityLink { get; init; }
        public int? GreaterEqualBuild { get; init; }
        public int? LessThanBuild { get; init; }
    }

    public record AbilityDetectionSettings
    {
        public AbilityDetection Taunt { get; init; }
        public AbilityDetection Dance { get; init; }
        public AbilityDetection Hearth { get; init; }
    }

    public record AbilityDetection
    {
        public int? CmdIndex { get; init; }
        public IEnumerable<AbilityBuild> AbilityBuilds { get; init; }
    }

    public class Settings
    {
        public ProcessSettings ProcessSettings { get; init; }
        public StormReplaySettings StormReplaySettings { get; init; }
        public FeatureToggleSettings FeatureToggleSettings { get; init; }
        public HeroesProfileApiSettings HeroesProfileApiSettings { get; init; }
        public SpectateWeightSettings SpectateWeightSettings { get; init; }
        public HotsApiSettings HotsApiSettings { get; init; }
        public TwitchApiSettings TwitchApiSettings { get; init; }
        public SpectateSettings SpectateSettings { get; init; }
        public CaptureSettings CaptureSettings { get; init; }
        public LocationSettings LocationSettings { get; init; }
        public OCRSettings OCRSettings { get; init; }
        public UnitSettings UnitSettings { get; init; }
        public MapSettings MapSettings { get; init; }
        public ParseOptionsSettings ParseOptionsSettings { get; init; }
        public AbilityDetectionSettings AbilityDetectionSettings { get; init; }

        public ParseOptions ParseOptions => new ParseOptions()
        {
            AllowPTR = false,
            IgnoreErrors = false,
            ShouldParseEvents = ParseOptionsSettings.ShouldParseEvents,
            ShouldParseMessageEvents = ParseOptionsSettings.ShouldParseMessageEvents,
            ShouldParseMouseEvents = ParseOptionsSettings.ShouldParseMouseEvents,
            ShouldParseDetailedBattleLobby = ParseOptionsSettings.ShouldParseDetailedBattleLobby,
            ShouldParseUnits = ParseOptionsSettings.ShouldParseUnits,
            ShouldParseStatistics = ParseOptionsSettings.ShouldParseStatistics
        };

        public string CurrentDirectory { get; } = Directory.GetCurrentDirectory();
        public string TempPath { get; } = Path.GetTempPath();
        public string AssetsPath => Path.Combine(CurrentDirectory, "Assets");
        public string StormInterfacePath => Path.Combine(AssetsPath, SpectateSettings.StormInterface);
        public string CurrentReplayInfoFilePath => Path.Combine(CurrentDirectory, StormReplaySettings.InfoFileName);
        public string StormReplaysAccountPath => Path.Combine(UserGameFolderPath, "Accounts");
        public string UserStormInterfacePath => Path.Combine(UserGameFolderPath, "Interfaces");
        public string StormReplayHotsApiCache => Path.Combine(TempPath, "HeroesReplay");
        public string UserGameFolderPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Heroes of the Storm");

    }
}