
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace HeroesReplay
{
    public sealed class GameProvider : Queue<string>
    {
        private readonly string input;
        private readonly string finishedPath;
        private readonly string invalidPath;

        public GameProvider() : this("G:\\replays\\input", "G:\\replays\\finished", "G:\\replays\\invalid")
        {

        }

        public GameProvider(string input, string finishedPath, string invalidPath)
        {
            this.input = input;
            this.finishedPath = finishedPath;
            this.invalidPath = invalidPath;

            Initialize();
        }

        private void Initialize()
        {
            foreach(var directory in new[] { input, finishedPath, invalidPath })
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

        public async Task MoveToInvalidAsync(Game game) => await MoveAsync(game.FilePath, invalidPath);

        public async Task MoveToFinishedAsync(Game game) => await MoveAsync(game.FilePath, finishedPath);

        private async Task MoveAsync(string sourcePath, string destinationPath)
        {
            await using (Stream source = File.Open(sourcePath, FileMode.Open))
            {
                await using (Stream destination = File.Create(Path.Combine(destinationPath)))
                {
                    await source.CopyToAsync(destination);
                }
            }
        }

        public void LoadReplays()
        {
            foreach (var file in Directory.GetFiles(input, "*.StormReplay"))
            {
                if (!Contains(file)) Enqueue(file);
            }
        }
    }
}
