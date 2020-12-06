
using HeroesReplay.Core;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
            var settings = new Settings { Weights = new SpectateWeightSettings() { } };
            replayAnalzer = new ReplayAnalyzer(new NullLogger<ReplayAnalyzer>(), settings, new[] { new PlayerKillsCalculator(settings) });
        }

        [Fact]
        public void CalculatorKills()
        {
            // arrange

            // act
            var results = replayAnalzer.GetPlayers(fixture.Replay);

            // assert
            var playerKills = results.Values.Where(x => x.Calculator == typeof(PlayerKillsCalculator)).GroupBy(x => x.Player.Character).ToDictionary(x => x.Key, x => x);


            Assert.True(playerKills["Valla"].Count() > 7);
        }
    }
}
