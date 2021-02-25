using System;
using System.IO;

namespace HeroesReplay.Core.Configuration
{
    public class AppSettings
    {
        public ProcessSettings Process { get; set; }
        public HeroesToolChestSettings HeroesToolChest { get; set; }
        public GithubSettings Github { get; set; }
        public OBSSettings OBS { get; set; }
        public StormReplaySettings StormReplay { get; set; }
        public HeroesProfileApiSettings HeroesProfileApi { get; set; }
        public HeroesProfileTwitchExtensionSettings TwitchExtension { get; set; }
        public TrackerEventSettings TrackerEvents { get; set; }
        public WeightSettings Weights { get; set; }
        public ReplayDetailsWriterSettings ReplayDetailsWriter { get; set; }
        public TwitchSettings Twitch { get; set; }
        public SpectateSettings Spectate { get; set; }
        public PanelTimesSettings PanelTimes { get; set; }
        public CaptureSettings Capture { get; set; }
        public LocationSettings Location { get; set; }
        public OCRSettings OCR { get; set; }
        public MapSettings Maps { get; set; }
        public ParseOptionsSettings ParseOptions { get; set; }
        public AbilityDetectionSettings AbilityDetection { get; set; }
        public QuoteSettings Quotes { get; set; }

        public string CurrentDirectory { get; } = Directory.GetCurrentDirectory();
        public string AssetsPath => Path.Combine(CurrentDirectory, "Assets");
        public string HeroesDataPath => Path.Combine(AssetsPath, "HeroesData");
        public string ReplayCachePath => Path.Combine(AssetsPath, "Replays");
        public string RequestedReplayCachePath => Path.Combine(AssetsPath, "RequestedReplays");
        public string SpectateReportPath => Path.Combine(AssetsPath, "SpectateReport");
        public string CapturesPath => Path.Combine(AssetsPath, "Captures");
        public string CurrentReplayInfoFilePath => Path.Combine(AssetsPath, StormReplay.InfoFileName);
        public static string StormReplaysAccountPath => Path.Combine(UserGameFolderPath, "Accounts");
        public static string UserStormInterfacePath => Path.Combine(UserGameFolderPath, "Interfaces");
        public static string UserGameFolderPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Heroes of the Storm");
    }
}