using System;
using System.IO;

namespace System.Runtime.CompilerServices
{
    public class IsExternalInit { }
}

namespace HeroesReplay.Core.Configuration
{
    public class AppSettings
    {
        public ProcessSettings Process { get; init; }
        public HeroesToolChestSettings HeroesToolChest { get; init; }
        public GithubSettings Github { get; init; }
        public OBSSettings OBS { get; init; }
        public StormReplaySettings StormReplay { get; init; }
        public HeroesProfileApiSettings HeroesProfileApi { get; init; }
        public WeightSettings Weights { get; init; }
        public ReplayDetailsWriterSettings ReplayDetailsWriter { get; init; }
        public TwitchApiSettings TwitchApi { get; init; }
        public SpectateSettings Spectate { get; init; }
        public PanelTimesSettings PanelTimes { get; init; }
        public CaptureSettings Capture { get; init; }
        public LocationSettings Location { get; init; }
        public OCRSettings OCR { get; init; }
        public MapSettings Maps { get; init; }
        public ParseOptionsSettings ParseOptions { get; init; }
        public AbilityDetectionSettings AbilityDetection { get; init; }
        public string CurrentDirectory { get; } = Directory.GetCurrentDirectory();
        public string AssetsPath => Path.Combine(CurrentDirectory, "Assets");
        public string HeroesDataPath => Path.Combine(AssetsPath, "HeroesData");
        public string ReplayCachePath => Path.Combine(AssetsPath, "Replays");
        public string AnalyzePath => Path.Combine(AssetsPath, "AnalyzeOutput");
        public string CapturesPath => Path.Combine(AssetsPath, "Captures");
        public string CurrentReplayInfoFilePath => Path.Combine(AssetsPath, StormReplay.InfoFileName);
        public static string StormReplaysAccountPath => Path.Combine(UserGameFolderPath, "Accounts");
        public static string UserStormInterfacePath => Path.Combine(UserGameFolderPath, "Interfaces");
        public static string UserGameFolderPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Heroes of the Storm");
    }
}