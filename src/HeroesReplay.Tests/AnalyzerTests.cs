using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace HeroesReplay.Tests
{
    public class AnalyzerTests
    {
        public AnalyzerTests()
        {
            IOptions<Settings> options = new OptionsWrapper<Settings>(new Settings { });
        }

        [Theory]
        [StormReplayFileData("hour-long-replay-provided-by-mgatner.StormReplay")]
        public void ShouldHave10AliveHeroes(StormReplay stormReplay)
        {

        }
    }
}
