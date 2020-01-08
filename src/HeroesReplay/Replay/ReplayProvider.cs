
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HeroesReplay
{
    public sealed class GameProvider : Queue<string>
    {
        private readonly string input;
        private readonly string finished;
        private readonly string invalid;

        public GameProvider() : this("G:\\replays\\input", "G:\\replays\\finished", "G:\\replays\\invalid")
        {

        }

        public GameProvider(string input, string finished, string invalid)
        {
            this.input = input;
            this.finished = finished;
            this.invalid = invalid;

            Initialize();
        }

        private void Initialize()
        {
            foreach(var directory in new[] { input, finished, invalid })
            {
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            }
        }

        public async Task<(bool Success, Game Game)> TryLoadAsync()
        {
            if (TryDequeue(out var path))
            {
                var (result, replay) = Heroes.ReplayParser.DataParser.ParseReplay(path, true, false, false, false);

                if (result != Heroes.ReplayParser.DataParser.ReplayParseResult.Exception && result != Heroes.ReplayParser.DataParser.ReplayParseResult.PreAlphaWipe && result != Heroes.ReplayParser.DataParser.ReplayParseResult.Incomplete)
                    return (Success: true, Game: new Game(path, replay));
            }

            return (Success: false, Game: null);
        }

        public void MoveToFinished(Game game) => File.Move(game.FilePath, Path.Combine(finished, Path.GetFileName(game.FilePath)));

        public void LoadReplays()
        {
            foreach (var file in Directory.GetFiles(input, "*.StormReplay"))
            {
                if (!Contains(file)) Enqueue(file);
            }
        }
    }
}
