
using HeroesReplay.Core.Processes;

namespace HeroesReplay.Core.Shared
{
    public record CaptureSettings
    {
        public CaptureMethod Method { get; init; }
        public bool SaveCaptureFailureCondition { get; init; }
    }
}