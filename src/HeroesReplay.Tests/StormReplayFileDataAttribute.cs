using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Heroes.ReplayParser;
using HeroesReplay.Core.Shared;
using Xunit.Sdk;

namespace HeroesReplay.Tests
{
    public class StormReplayFileDataAttribute : DataAttribute
    {
        private string path;
        private ParseOptions parseOptions;

        public StormReplayFileDataAttribute(string path)
        {
            this.path = path;

            this.parseOptions = new ParseOptions()
            {
                ShouldParseEvents = true,
                ShouldParseMessageEvents = true,
                ShouldParseStatistics = true,
                ShouldParseMouseEvents = true,
                ShouldParseUnits = true
            };
        }

        public override IEnumerable<object[]> GetData(MethodInfo testMethod)
        {
            (DataParser.ReplayParseResult replayParseResult, Replay replay) = DataParser.ParseReplay(File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "Assets", path)), parseOptions);

            yield return new object[] { new StormReplay(path, replay) };
        }
    }
}