
using HeroesReplay.Core;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging.Abstractions;

using System.Linq;

using Xunit;

namespace HeroesReplay.Tests
{

    public class AnalyzerTests : IClassFixture<ReplayFixture>
    {
        private ReplayFixture fixture;

        private IReplayAnalzer replayAnalzer;

        public AnalyzerTests(ReplayFixture fixture)
        {
            this.fixture = fixture;
            var settings = new Settings { Weights = new WeightSettings() { } };
            replayAnalzer = new ReplayAnalyzer(new NullLogger<ReplayAnalyzer>(), settings, new[] { new PlayerKillsCalculator(settings, null) }, null);
        }

        [Fact]
        public void CalculatorKills()
        {
            // arrange

            // act
            var results = replayAnalzer.GetPlayers(fixture.Replay);

            // assert
            var playerKills = results.Values.Where(x => x.Calculator == typeof(PlayerKillsCalculator)).GroupBy(x => x.Target.Character).ToDictionary(x => x.Key, x => x);


            Assert.True(playerKills["Valla"].Count() > 7);
        }
    }
}
