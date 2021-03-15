namespace HeroesReplay.Core.Configuration
{
    public class LocationOptions
    {
        public string BattlenetPath { get; set; }
        public string ReplaySource { get; set; }
        public string DataDirectory { get; set; }

        public string ContextsFolder { get; set; }
        public string StandardReplaysFolder { get; set; }
        public string RequestedReplaysFolder { get; set; }
        public string GameInstallDirectory { get; set; }
    }
}