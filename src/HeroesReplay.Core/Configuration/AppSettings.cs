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
        public YouTubeSettings YouTube { get; set; }
        public string CurrentDirectory { get; } = Directory.GetCurrentDirectory();
        public string AssetsPath => Path.Combine(CurrentDirectory, "Assets");
        public string ContextsDirectory => Path.Combine(Location.DataDirectory, "Contexts");
        public string HeroesDataPath => Path.Combine(Location.DataDirectory, "HeroesData");
        public string StandardReplayCachePath => Path.Combine(Location.DataDirectory, HeroesProfileApi.StandardCacheDirectoryName);
        public string RequestedReplayCachePath => Path.Combine(Location.DataDirectory, HeroesProfileApi.RequestsCacheDirectoryName);
        public string SpectateReportPath => Path.Combine(Location.DataDirectory, "SpectateReport");
        public string CapturesPath => Path.Combine(Location.DataDirectory, "Capture");
        public static string StormReplaysAccountPath => Path.Combine(UserGameFolderPath, "Accounts");
        public static string UserStormInterfacePath => Path.Combine(UserGameFolderPath, "Interfaces");
        public static string UserGameFolderPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Heroes of the Storm");
    }
}