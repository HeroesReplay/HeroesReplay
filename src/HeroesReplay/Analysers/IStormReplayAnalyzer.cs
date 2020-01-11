using System;

namespace HeroesReplay
{
    public interface IStormReplayAnalyzer
    {
        AnalyzerResult Analyze(StormReplay stormReplay, TimeSpan start, TimeSpan end);
    }
}