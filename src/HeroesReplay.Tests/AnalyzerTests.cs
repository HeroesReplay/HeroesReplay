using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;
using HeroesReplay.Core.Services.Analysis;
using HeroesReplay.Core.Services.Analysis.Calculators;
using Xunit;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Tests
{
    public class AnalyzerTests : IClassFixture<ReplayFixture>
    {
        private readonly ReplayFixture fixture;
        private readonly IReplayAnalyzer replayAnalzer;

        public AnalyzerTests(ReplayFixture fixture)
        {
            this.fixture = fixture;
            var settings = Options.Create(new AppSettings { Weights = new WeightOptions() { } });
            
            replayAnalzer = new ReplayAnalyzer(new NullLogger<ReplayAnalyzer>(), settings, null, new[] { new KillCalculator(settings) }, null);
        }

        [Fact]
        public void CalculatorKills()
        {
            // arrange

            // act
            var results = replayAnalzer.GetPlayers(fixture.Replay);

            // assert
            var playerKills = results.Values.Where(x => x.Calculator == typeof(KillCalculator)).GroupBy(x => x.Target.Character).ToDictionary(x => x.Key, x => x);


            Assert.True(playerKills["Valla"].Count() > 7);
        }
    }
}
