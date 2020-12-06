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
        public string Battlenet { get; init; }
        public string HeroesOfTheStorm { get; init; }
    }

    public record StormReplaySettings
    {
        public string InfoFileName { get; init; }
        public string WildCard { get; init; }
        public string FileExtension { get; init; }
    }

    public record HeroesProfileApiSettings
    {
        public Uri BaseUri { get; init; }
        public string ApiKey { get; init; }
    }

    public record HotsApiSettings
    {
        public string AwsAccessKey { get; init; }
        public string AwsSecretKey { get; init; }
        public int ReplayIdUnset { get; init; }
        public int MinReplayId { get; init; }
        public int ReplayIdBaseline { get; init; }
        public Uri BaseUri { get; init; }
        public string CachedFileNameSplitter { get; init; }
        public IEnumerable<string> GameTypes { get; init; }
    }

    public record TwitchApiSettings
    {
        public string AccessToken { get; init; }
        public string ClientId { get; init; }
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
        public CaptureMethod Method { get; init; }
        public string ConditionFailurePath { get; init; }
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
        public ProcessSettings Process { get; init; }
        public StormReplaySettings StormReplay { get; init; }
        public FeatureToggleSettings Toggles { get; init; }
        public HeroesProfileApiSettings HeroesProfileApi { get; init; }
        public SpectateWeightSettings Weights { get; init; }
        public HotsApiSettings HotsApi { get; init; }
        public TwitchApiSettings TwitchApi { get; init; }
        public SpectateSettings Spectate { get; init; }
        public CaptureSettings Capture { get; init; }
        public LocationSettings Location { get; init; }
        public OCRSettings OCR { get; init; }
        public UnitSettings Units { get; init; }
        public MapSettings Maps { get; init; }
        public ParseOptionsSettings ParseOptions { get; init; }
        public AbilityDetectionSettings AbilityDetection { get; init; }       

        public string CurrentDirectory { get; } = Directory.GetCurrentDirectory();
        public string TempPath { get; } = Path.GetTempPath();
        public string AssetsPath => Path.Combine(CurrentDirectory, "Assets");
        public string StormInterfacePath => Path.Combine(AssetsPath, Spectate.StormInterface);
        public string CurrentReplayInfoFilePath => Path.Combine(CurrentDirectory, StormReplay.InfoFileName);
        public string StormReplaysAccountPath => Path.Combine(UserGameFolderPath, "Accounts");
        public string UserStormInterfacePath => Path.Combine(UserGameFolderPath, "Interfaces");
        public string StormReplayHotsApiCache => Path.Combine(TempPath, "HeroesReplay");
        public string UserGameFolderPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Heroes of the Storm");

    }
}