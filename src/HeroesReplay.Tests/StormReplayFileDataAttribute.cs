using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Heroes.ReplayParser;
using HeroesReplay.Shared;
using HeroesReplay.Spectator;
using Xunit.Sdk;

namespace HeroesReplay.Tests
{
    public class StormReplayFileDataAttribute : DataAttribute
    {
        private readonly string path;

        public StormReplayFileDataAttribute(string path)
        {
            this.path = path;
        }

        public override IEnumerable<object[]> GetData(MethodInfo testMethod)
        {
            (DataParser.ReplayParseResult replayParseResult, Replay replay) = DataParser.ParseReplay(File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "replays", path)), Constants.REPLAY_PARSE_OPTIONS);

            yield return new object[] { new StormReplay(path, replay) };
        }
    }
}