using System;
using System.IO;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.SelfUpdate;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.ServiceHost.Logs;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating;
using HeroesReplay.Core.Spectating.Capture;
using HeroesReplay.Core.Spectating.Screens;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.TwitchExtension;
using HeroesReplay.Core.YouTube;

namespace HeroesReplay.Core.Configuration;

public class AppSettings
{
    public ProcessSettings Process { get; set; }
    public HeroesToolChestSettings HeroesToolChest { get; set; }
    public FocusUnitSettings FocusUnits { get; set; }
    public GithubSettings Github { get; set; }
    public OBSSettings OBS { get; set; }
    public ConnectivitySettings Connectivity { get; set; }
    public StormReplaySettings StormReplay { get; set; }
    public HeroesProfileApiSettings HeroesProfileApi { get; set; }
    public HeroesProfileTwitchExtensionSettings TwitchExtension { get; set; }
    public TrackerEventSettings TrackerEvents { get; set; }
    public WeightSettings Weights { get; set; }
    public CalculatorSettings Calculators { get; set; }
    public ReplayDetailsWriterSettings ReplayDetailsWriter { get; set; }
    public TwitchSettings Twitch { get; set; }
    public SpectateSettings Spectate { get; set; }
    public PanelTimesSettings PanelTimes { get; set; }
    public CaptureSettings Capture { get; set; }
    public LocationSettings Location { get; set; }
    public ClientSettings Client { get; set; }
    public OCRSettings OCR { get; set; }
    public MapSettings Maps { get; set; }
    public ParseOptionsSettings ParseOptions { get; set; }
    public AbilityDetectionSettings AbilityDetection { get; set; }
    public YouTubeSettings YouTube { get; set; }
    public DiskBacklogSettings Disk { get; set; } = SpectateAdmission.DefaultWatermarks();
    public ReplayMediaPolicySettings ReplayMedia { get; set; } = new ReplayMediaPolicySettings();
    public RetentionSettings Retention { get; set; }
    public ReleaseSettings Release { get; set; }
    public ServiceHealthSettings ServiceHealth { get; set; } = new ServiceHealthSettings();
    public ServiceLogSettings ServiceLogs { get; set; } = new ServiceLogSettings();
    public ServiceRestartSettings ServiceRestart { get; set; } = new ServiceRestartSettings();
    public BattleNetAgentSettings BattleNetAgents { get; set; } = new BattleNetAgentSettings();
    public MachineHealthSettings MachineHealth { get; set; } = new MachineHealthSettings();
    public string CurrentDirectory { get; } = Directory.GetCurrentDirectory();
    public string AssetsPath => Path.Combine(CurrentDirectory, "Assets");
    public string ContextsDirectory => Path.Combine(Location.DataDirectory, "Contexts");
    public string HeroesDataPath => Path.Combine(Location.DataDirectory, "HeroesData");
    public string StandardReplayCachePath =>
        Path.Combine(Location.DataDirectory, HeroesProfileApi.StandardCacheDirectoryName);
    public string RequestedReplayCachePath =>
        Path.Combine(Location.DataDirectory, HeroesProfileApi.RequestsCacheDirectoryName);
    public string SpectateReportPath => Path.Combine(Location.DataDirectory, "SpectateReport");
    public string CapturesPath => Path.Combine(Location.DataDirectory, "Capture");
    public static string StormReplaysAccountPath => Path.Combine(UserGameFolderPath, "Accounts");
    public static string UserStormInterfacePath => Path.Combine(UserGameFolderPath, "Interfaces");
    public static string UserGameFolderPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Heroes of the Storm"
        );
}
