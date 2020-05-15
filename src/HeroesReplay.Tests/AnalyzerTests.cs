using System;
using System.Linq;
using HeroesReplay.Core.Analyzer;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HeroesReplay.Tests
{
    public class AnalyzerTests
    {
        private readonly StormReplayAnalyzer analyzer;

        public AnalyzerTests()
        {
            IOptions<Settings> options = new OptionsWrapper<Settings>(new Settings { });
            ReplayHelper helper = new ReplayHelper(new NullLogger<ReplayHelper>(), options, new GameDataService());
            this.analyzer = new StormReplayAnalyzer(new NullLogger<StormReplayAnalyzer>(), options, helper);
        }

        [Theory]
        [StormReplayFileData("hour-long-replay-provided-by-mgatner.StormReplay")]
        public void ShouldHave10AliveHeroes(StormReplay stormReplay)
        {
            AnalyzerResult analyzerResult = analyzer.Analyze(stormReplay.Replay, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));

            Assert.Equal(10, analyzerResult.Alive.Count());
        }
    }
}
