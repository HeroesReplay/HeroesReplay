
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HeroesReplay
{
    public sealed class GameProvider : Queue<string>
    {
        private readonly ILogger<GameProvider> logger;
        private readonly IConfiguration configuration;

        public GameProvider(ILogger<GameProvider> logger, IConfiguration configuration)
        {
            this.logger = logger;
            this.configuration = configuration;
        }

        public async Task<(bool Success, Game Game)> TryLoadAsync()
        {
            if (TryDequeue(out var path))
            {
                logger.LogInformation("Dequeued: " + path);

                var (result, replay) = Heroes.ReplayParser.DataParser.ParseReplay(path, true, false, false, false);

                if (result != Heroes.ReplayParser.DataParser.ReplayParseResult.Exception && result != Heroes.ReplayParser.DataParser.ReplayParseResult.PreAlphaWipe && result != Heroes.ReplayParser.DataParser.ReplayParseResult.Incomplete)
                {
                    logger.LogInformation("Parse Success: " + path);
                    return (Success: true, Game: new Game(path, replay));
                }
                else
                {
                    logger.LogInformation("Parse Error: " + path);
                    logger.LogInformation("Result: " + result);
                }
            }

            return (Success: false, Game: null);
        }

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
            if (configuration.GetValue<bool>("replays:load.replay.files.user.documents"))
            {
                var replays = Directory
                    .GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Heroes of the Storm", "Accounts"), "*.StormReplay", SearchOption.AllDirectories)
                    .OrderBy(replay => File.GetCreationTime(replay));

                foreach (var replay in replays)
                {
                    if (!Contains(replay))
                    {
                        logger.LogInformation("Queued: " + replay);
                        Enqueue(replay);
                    }
                }
            }
        }
    }
}
