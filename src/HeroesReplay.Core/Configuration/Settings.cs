
using System;
using System.IO;

namespace System.Runtime.CompilerServices
{
    public class IsExternalInit { }
}

namespace HeroesReplay.Core.Shared
{

    public class Settings
    {
        public ProcessSettings Process { get; init; }
        public HeroesToolChestSettings HeroesToolChest { get; init; }
        public GithubSettings Github { get; set; }
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