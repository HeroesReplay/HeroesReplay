using System;
using System.IO;
using Heroes.ReplayParser;
using HeroesReplay.Shared;
using HeroesReplay.Spectator;

namespace HeroesReplay.Tests
{
    public class StormReplayFixture : IDisposable
    {
        public StormReplay StormReplay { get; }

        public StormReplayFixture(string filePath = @"hour-long-replay-provided-by-mgatner.StormReplay")
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "replays", filePath);

            (DataParser.ReplayParseResult replayParseResult, Replay replay) = DataParser.ParseReplay(File.ReadAllBytes(path), Constants.REPLAY_PARSE_OPTIONS);

            StormReplay = new StormReplay(path, replay);
        }

        public void Dispose()
        {

        }
    }
}