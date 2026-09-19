using System.Diagnostics;

namespace HeroesReplay.Core;

public static class HeroesReplayTelemetry
{
    public const string SourceName = "HeroesReplay";

    public static readonly ActivitySource ActivitySource = new(SourceName);
}
