using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Configuration
{
    public class CaptureOptions
    {
        public CaptureMethod Method { get; set; }
        public bool SaveTimerRegion { get; set; }
        public bool SaveCaptureFailureCondition { get; set; }
    }
}