using System;
using System.IO;
using Heroes.ReplayParser;
using HeroesReplay.Spectator;

namespace HeroesReplay.Tests
{
    public class StormReplayFixture : IDisposable
    {
        public StormReplay StormReplay { get; }

        public StormReplayFixture(string filePath = @"hour-long-replay-provided-by-mgatner.StormReplay")
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "replays", filePath);

            (DataParser.ReplayParseResult replayParseResult, Replay replay) = DataParser.ParseReplay(File.ReadAllBytes(path), ignoreErrors: true, allowPTRRegion: false);

            StormReplay = new StormReplay(path, replay);
        }

        public void Dispose()
        {

        }
    }
}