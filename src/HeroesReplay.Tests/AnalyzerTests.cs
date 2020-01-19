using System;
using HeroesReplay.Spectator;
using Xunit;

namespace HeroesReplay.Tests
{
    public class AnalyzerTests : IClassFixture<StormReplayFixture>
    {
        private readonly StormReplayAnalyzer analyzer;
        private readonly StormReplayFixture fixture;

        public AnalyzerTests(StormReplayFixture fixture)
        {
            this.fixture = fixture;
            this.analyzer = new StormReplayAnalyzer();
        }

        [Fact]
        public void ShouldHave10AliveHeroes()
        {
            AnalyzerResult analyzerResult = analyzer.Analyze(fixture.StormReplay, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));

            Assert.Equal(10, analyzerResult.PlayersAlive.Count);
        }
    }
}
