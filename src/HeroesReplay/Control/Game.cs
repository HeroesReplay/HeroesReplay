using System;
using System.IO;
using Heroes.ReplayParser;
using static Heroes.ReplayParser.DataParser;

namespace HeroesReplay
{
    /// <summary>
    /// The Game is a wrapper which links a raw replay file on disk to an in-memory parsed version of that file
    /// </summary>
    public class Game
    {
        public Lazy<(ReplayParseResult Result, Replay Replay)> Parsed { get; }
        public Replay Replay => Parsed.Value.Replay;
        public ReplayParseResult Result => Parsed.Value.Result;
        public string Path { get; }
        public bool IsValid => Result == ReplayParseResult.Success;

        public Game(string path)
        {
            Parsed = new Lazy<(ReplayParseResult Result, Replay Replay)>(() => 
            { 
                var (result, replay) = ParseReplay(File.ReadAllBytes(path)); 
                return (result, replay);
            });

            Path = path;
        }
    }
}