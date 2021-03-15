using System;
using System.IO;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using AbilityDetectionOptions = HeroesReplay.Service.Spectator.Core.Configuration.AbilityDetectionOptions;
using CaptureOptions = HeroesReplay.Service.Spectator.Core.Configuration.CaptureOptions;
using ContextManagerOptions = HeroesReplay.Service.Spectator.Core.Configuration.ContextManagerOptions;
using GithubOptions = HeroesReplay.Service.Spectator.Core.Configuration.GithubOptions;
using HeroesProfileApiOptions = HeroesReplay.Service.Spectator.Core.Configuration.HeroesProfileApiOptions;
using TwitchExtensionOptions = HeroesReplay.Service.Spectator.Core.Configuration.TwitchExtensionOptions;
using HeroesToolChestOptions = HeroesReplay.Service.Spectator.Core.Configuration.HeroesToolChestOptions;
using LocationOptions = HeroesReplay.Service.Spectator.Core.Configuration.LocationOptions;
using MapOptions = HeroesReplay.Service.Spectator.Core.Configuration.MapOptions;
using OcrOptions = HeroesReplay.Service.Spectator.Core.Configuration.OcrOptions;
using PanelTimesOptions = HeroesReplay.Service.Spectator.Core.Configuration.PanelTimesOptions;
using ProcessOptions = HeroesReplay.Service.Spectator.Core.Configuration.ProcessOptions;
using QueueOptions = HeroesReplay.Service.Spectator.Core.Configuration.QueueOptions;
using SpectateOptions = HeroesReplay.Service.Spectator.Core.Configuration.SpectateOptions;
using StormReplayOptions = HeroesReplay.Service.Spectator.Core.Configuration.StormReplayOptions;
using TrackerEventOptions = HeroesReplay.Service.Spectator.Core.Configuration.TrackerEventOptions;
using TwitchOptions = HeroesReplay.Service.Spectator.Core.Configuration.TwitchOptions;

namespace HeroesReplay.Service.Spectator
{
    public class AppSettings
    {
        public ProcessOptions Process { get; set; }
        public HeroesToolChestOptions HeroesToolChest { get; set; }
        public GithubOptions Github { get; set; }
        public StormReplayOptions StormReplay { get; set; }
        public HeroesProfileApiOptions HeroesProfileApi { get; set; }
        public TwitchExtensionOptions TwitchExtension { get; set; }
        public TrackerEventOptions TrackerEvents { get; private set; } = new();
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