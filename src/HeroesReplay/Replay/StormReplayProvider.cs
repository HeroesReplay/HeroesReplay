using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static Heroes.ReplayParser.DataParser;

namespace HeroesReplay
{
    public sealed class StormReplayProvider
    {
        public int Count => queue.Count;

        private readonly ILogger<StormReplayProvider> logger;
        private readonly IConfiguration configuration;
        private readonly Queue<string> queue;
        private readonly string replaysDirectory;

        private const string FileExtension = "*.StormReplay";

        public StormReplayProvider(ILogger<StormReplayProvider> logger, IConfiguration configuration)
        {
            this.logger = logger;
            this.configuration = configuration;
            this.queue = new Queue<string>();
            this.replaysDirectory = configuration.GetValue<string>("replays", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Heroes of the Storm", "Accounts"));
        }

        public async Task<StormReplay?> TryLoadReplayAsync(string path)
        {
            byte[] bytes = await File.ReadAllBytesAsync(path);
            (ReplayParseResult result, Replay replay) = ParseReplay(bytes, true, false);

            if (result != ReplayParseResult.Exception && result != ReplayParseResult.PreAlphaWipe && result != ReplayParseResult.Incomplete)
            {
                logger.LogInformation("Parse Success: " + path);
                return new StormReplay(path, replay);
            }

            return null;
        }

        public async Task<StormReplay?> TryLoadReplayAsync()
        {
            if (queue.TryDequeue(out var path))
            {
                logger.LogInformation("Dequeued: " + path);

                var (result, replay) = DataParser.ParseReplay(path, true, false);

                if (result != DataParser.ReplayParseResult.Exception && result != DataParser.ReplayParseResult.PreAlphaWipe && result != DataParser.ReplayParseResult.Incomplete)
                {
                    logger.LogInformation("Parse Success: " + path);
                    return new StormReplay(path, replay);
                }

                logger.LogInformation("Parse Error: " + path);
                logger.LogInformation("Result: " + result);
            }

            return null;
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
            foreach (string file in Directory.GetFiles(replaysDirectory, FileExtension, SearchOption.AllDirectories).OrderBy(File.GetCreationTime))
            {
                if (!queue.Contains(file))
                {
                    logger.LogInformation("Queued: " + file);
                    queue.Enqueue(file);
                }
            }
        }
    }
}
