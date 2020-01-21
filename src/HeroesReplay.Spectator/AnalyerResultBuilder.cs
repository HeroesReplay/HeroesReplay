using System;
using HeroesReplay.Analyzer;
using HeroesReplay.Shared;

namespace HeroesReplay.Spectator
{
    public sealed class AnalyerResultBuilder
    {
        private StormReplayAnalyzer? analyzer;
        private TimeSpan start;
        private StormReplay? stormReplay;
        private StormReplaySpectator? spectator;

        public AnalyerResultBuilder WithAnalyzer(StormReplayAnalyzer analyzer)
        {
            this.analyzer = analyzer;
            return this;
        }

        public AnalyerResultBuilder WithStormReplay(StormReplay stormReplay)
        {
            this.stormReplay = stormReplay;
            return this;
        }

        public AnalyerResultBuilder WithSpectator(StormReplaySpectator stormReplaySpectator)
        {
            this.spectator = stormReplaySpectator;
            return this;
        }

        public AnalyerResultBuilder WithStart(TimeSpan start)
        {
            this.start = start;
            return this;
        }

        public AnalyzerResult Check(TimeSpan timeSpan)
        {
            if (spectator != null)
            {
                return analyzer.Analyze(spectator.StormReplay, spectator.GameTimer.Duration(), spectator.GameTimer.Add(timeSpan));
            }

            return analyzer.Analyze(stormReplay, start, start.Add(timeSpan));
        }
    }
}