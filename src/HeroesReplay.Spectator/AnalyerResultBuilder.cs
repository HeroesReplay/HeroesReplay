using System;
using HeroesReplay.Analyzer;
using HeroesReplay.Shared;

namespace HeroesReplay.Spectator
{
    public sealed class AnalyerResultBuilder
    {
        private StormReplayAnalyzer analyzer;
        private TimeSpan start;
        private StormReplay stormReplay;
        private StormReplaySpectator? spectator;

        public AnalyerResultBuilder WithAnalyzer(StormReplayAnalyzer analyzer)
        {
            this.analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            return this;
        }

        public AnalyerResultBuilder WithStormReplay(StormReplay stormReplay)
        {
            this.stormReplay = stormReplay ?? throw new ArgumentNullException(nameof(stormReplay));
            return this;
        }

        public AnalyerResultBuilder WithSpectator(StormReplaySpectator spectator)
        {
            this.spectator = spectator ?? throw new ArgumentNullException(nameof(spectator));
            return this;
        }

        public AnalyerResultBuilder WithStart(TimeSpan start)
        {
            this.start = start;
            return this;
        }

        public AnalyzerResult Check(TimeSpan timeSpan)
        {
            return spectator != null ? 
                analyzer.Analyze(spectator.StormReplay, spectator.GameTimer.Duration(), spectator.GameTimer.Add(timeSpan)) : 
                analyzer.Analyze(stormReplay, start, start.Add(timeSpan));
        }
    }
}