using System;

namespace HeroesReplay.Core.Shared
{
    public class Settings
    {
        public int MaxDistanceToCore { get; set; }
        public int MaxDistanceToEnemy { get; set; }
        public int MaxDistanceToObjective { get; set; }
        public int MaxDistanceToOwnerChange { get; set; }

        public string StormInterface { get; set; }

        public Uri HeroesProfileBaseUri { get; set; }
        public Version MinVersionSupported { get; set; }
        public TimeSpan KillTime { get; set; }
        public TimeSpan EndScreenTime { get; set; }

        public int[] TalentLevels { get; set; }
        public string[] CarriedObjectiveMaps { get; set; }

        public bool CaptureSaveFailure { get; set; }
        public string CaptureSavePath { get; set; }

        public int GameLoopsOffset { get; set; }
        public int GameLoopsPerSecond { get; set; }

        public int ReplayIdBaseline { get; set; }
        public int ReplayIdUnset { get; set; }


        public string[] CoreUnitNames { get; set; }
        public string[] BossUnitNames { get; set; }
        public string[] CampUnitNames { get; set; }

        public string[] MapObjectiveUnitNames { get; set; }
        public string[] CaptureUnitNames { get; set; }

        public TimeSpan MaxQuintupleTime => KillTime * 4;
        public TimeSpan MaxQuadTime => KillTime * 3;
        public TimeSpan MaxTripleTime => KillTime * 2;
        public TimeSpan MaxMultiTime => KillTime * 1;
    }
}
