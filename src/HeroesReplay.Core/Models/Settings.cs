using HeroesReplay.Core.Processes;
using System;
using System.IO;

namespace HeroesReplay.Core.Shared
{
    public class Settings
    {
        public string AwsAccessKey { get; set; }
        public string AwsSecretKey { get; set; }
        public string HeroesProfileApiKey { get; set; }
        public string TwitchBroadcasterId { get; set; }
        public string TwitchAccessToken { get; set; }
        public bool EnableTwitchClips { get; set; }
        public string TwitchClientId { get; set; }

        public int MaxDistanceToCore { get; set; }
        public int MaxDistanceToEnemy { get; set; }
        public int MaxDistanceToObjective { get; set; }
        public int MaxDistanceToOwnerChange { get; set; }
        public string StormInterface { get; set; }

        public bool EnableMMR { get; set; }
        public Uri HeroesProfileBaseUri { get; set; }
        public Version MinVersionSupported { get; set; }
        public TimeSpan KillTime { get; set; }
        public TimeSpan EndScreenTime { get; set; }
        public CaptureMethod CaptureMethod { get; set; }
        public int[] TalentLevels { get; set; }
        public string[] CarriedObjectiveMaps { get; set; }

        public bool CaptureSaveFailure { get; set; }
        public string CaptureSavePath { get; set; }

        public int GameLoopsOffset { get; set; }
        public int GameLoopsPerSecond { get; set; }

        public int MinReplayId { get; set; }
        public int ReplayIdBaseline { get; set; }
        public int ReplayIdUnset { get; set; }
        public string ReplaySource { get; set; }
        public string[] CoreUnitNames { get; set; }
        public string[] BossUnitNames { get; set; }
        public string[] CampUnitNames { get; set; }

        public string[] MapObjectiveUnitNames { get; set; }
        public string[] CaptureUnitNames { get; set; }
        public bool Launch { get; set; }

        public TimeSpan MaxQuintupleTime => KillTime * 4;
        public TimeSpan MaxQuadTime => KillTime * 3;
        public TimeSpan MaxTripleTime => KillTime * 2;
        public TimeSpan MaxMultiTime => KillTime * 1;

        public string CurrentDirectory = Directory.GetCurrentDirectory();

        public string TempPath = Path.GetTempPath();
        public string AssetsPath => Path.Combine(CurrentDirectory, "Assets");
        public string StormInterfacePath => Path.Combine(AssetsPath, StormInterface);
        public string CurrentReplayPath => Path.Combine(CurrentDirectory, Constants.STORM_REPLAY_INFO_FILE);
        public string StormReplaysAccountPath => Path.Combine(UserGameFolderPath, "Accounts");
        public string UserGameFolderPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Heroes of the Storm");
        public string UserStormInterfacePath => Path.Combine(UserGameFolderPath, "Interfaces", StormInterface);
        public string StormReplayCachePath => Path.Combine(TempPath, "HeroesReplay");
    }
}