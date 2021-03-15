using HeroesReplay.Service.Spectator.Core.Observer;

namespace HeroesReplay.Service.Spectator.Core.Configuration
{
    public class CaptureOptions
    {
        public CaptureMethod Method { get; set; }
        public bool SaveTimerRegion { get; set; }
        public bool SaveCaptureFailureCondition { get; set; }
    }
}