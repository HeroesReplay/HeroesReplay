using System;

namespace HeroesReplay
{
    public sealed class AnalyerResultBuilder
    {
        private IStormReplayAnalyzer? analyzer;
        private TimeSpan start;
        private StormReplay? stormReplay;
        private Spectator? spectator;

        public AnalyerResultBuilder WithAnalyzer(IStormReplayAnalyzer analyzer)
        {
            this.analyzer = analyzer;
            return this;
        }

        public AnalyerResultBuilder WithStormReplay(StormReplay stormReplay)
        {
            this.stormReplay = stormReplay;
            return this;
        }

        public AnalyerResultBuilder WithSpectator(Spectator spectator)
        {
            this.spectator = spectator;
            return this;
        }

        public AnalyerResultBuilder WithStart(TimeSpan start)
        {
            this.start = start;
            return this;
        }

        public AnalyzerResult Seconds(int seconds)
        {
            return spectator != null ?
                analyzer.Analyze(spectator.StormReplay, spectator.Timer, spectator.Timer.Add(TimeSpan.FromSeconds(seconds))) :
                analyzer.Analyze(stormReplay, start, start.Add(TimeSpan.FromSeconds(seconds)));
        }
    }
}