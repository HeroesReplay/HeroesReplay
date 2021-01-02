using HeroesReplay.Core.Processes;

namespace HeroesReplay.Core.Configuration
{
    public record CaptureSettings
    {
        public CaptureMethod Method { get; init; }
        public bool SaveTimerRegion { get; init; }
        public bool SaveCaptureFailureCondition { get; init; }
    }
}