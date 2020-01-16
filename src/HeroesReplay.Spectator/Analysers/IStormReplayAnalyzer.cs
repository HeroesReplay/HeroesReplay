using System;

namespace HeroesReplay.Spectator
{
    public interface IStormReplayAnalyzer
    {
        AnalyzerResult Analyze(StormReplay stormReplay, TimeSpan start, TimeSpan end);
    }
}