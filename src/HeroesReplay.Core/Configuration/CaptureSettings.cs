
using HeroesReplay.Core.Processes;

namespace HeroesReplay.Core.Shared
{
    public record CaptureSettings
    {
        public CaptureMethod Method { get; init; }
        public string ConditionFailurePath { get; init; }
    }
}