
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace HeroesReplay
{

    public class GameProvider : Queue<Game>
    {
        private readonly string input;
        private readonly string finished;
        private readonly string invalid;

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

        public new bool TryDequeue(out Game game)
        {
            bool result = false;

            if (base.TryDequeue(out game))
            {
                result = game.IsValid;
                
                if (!game.IsValid)
                {
                    var before = game.Path;
                    var after = Path.Combine(invalid, Path.GetFileName(game.Path));

                    File.Move(before, after);

                    File.WriteAllLines(Path.Combine(invalid, Path.GetFileName(after)) + ".invalid.txt", new[]
                    {
                        $"Result: {game.Result}",
                        $"Version: {Assembly.GetAssembly(typeof(Heroes.ReplayParser.DataParser)).GetName().Version}",
                        $"File: {game.Path}"
                    });
                }   
            }

            return result;
        }

        public void MoveToFinished(Game game) => File.Move(game.Path, Path.Combine(finished, Path.GetFileName(game.Path)));

        public void LoadReplays()
        {
            foreach (var file in Directory.GetFiles(input, "*.StormReplay").OrderBy(random => Guid.NewGuid()))
            {
                if (this.Any(g => g.Path == file)) continue;
                this.Enqueue(new Game(file));
            }
        }
    }
}
