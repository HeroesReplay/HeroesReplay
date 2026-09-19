using System.IO;
using Heroes.ReplayParser;

namespace HeroesReplay.Tests.Unit.Support;

public class ReplayFixture
{
    public Replay Replay { get; }

    public ReplayFixture()
    {
        var bytes = File.ReadAllBytes(
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "Assets",
                "hour-long-replay-provided-by-mgatner.StormReplay"
            )
        );
        var parseOptions = new ParseOptions
        {
            ShouldParseEvents = true,
            ShouldParseUnits = true,
            ShouldParseStatistics = true,
        };
        var result = DataParser.ParseReplay(bytes, parseOptions);
        Replay = result.Item2;
    }
}
