using Heroes.ReplayParser;

using System;
using System.IO;

namespace HeroesReplay.Core.Configuration
{
    public class AppSettings
    {
        public ProcessOptions Process { get; set; }
        public HeroesToolChestOptions HeroesToolChest { get; set; }
        public GithubOptions Github { get; set; }
        public ObsOptions Obs { get; set; }
        public StormReplayOptions StormReplay { get; set; }
        public HeroesProfileApiOptions HeroesProfileApi { get; set; }
        public HeroesProfileTwitchExtensionOptions TwitchExtension { get; set; }
        public TrackerEventOptions TrackerEvents { get; private set; } = new TrackerEventOptions();
        public WeightOptions Weights { get; set; }
        public ContextManagerOptions ContextManager { get; set; }
        public TwitchOptions Twitch { get; set; }
        public SpectateOptions Spectate { get; set; }
        public PanelTimesOptions PanelTimes { get; set; }
        public CaptureOptions Capture { get; set; }
        public LocationOptions Location { get; set; }
        public OcrOptions Ocr { get; set; }
        public MapOptions Maps { get; set; }
        public QueueOptions Queue { get; set; }
        public AbilityDetectionOptions AbilityDetection { get; set; }
        public QuoteOptions Quotes { get; set; }
        public YouTubeOptions YouTube { get; set; }
        public ParseOptions Parser { get; set; }
        public string CurrentDirectory { get; set; }
        public string AssetsPath { get; set; }
        public string ContextsDirectory { get; set; }
        public string HeroesDataPath { get; set; }
        public string StandardReplayCachePath { get; set; }
        public string RequestedReplayCachePath { get; set; }
        public string SpectateReportPath { get; set; }
        public string CapturesPath { get; set; }
        public string StormReplaysAccountPath { get; set; }
        public string UserStormInterfacePath { get; set; }
        public string UserGameFolderPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Heroes of the Storm");


    }
}