namespace HeroesReplay.Core.Services.YouTube
{

    using HeroesReplay.Core.Configuration;
    using HeroesReplay.Core.Models;

    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    public class FakeYouTubeUploader : IYouTubeUploader
    {
        private readonly ILogger<FakeYouTubeUploader> logger;
        private readonly IOptions<AppSettings> settings;
        private readonly CancellationTokenSource cts;

        public FakeYouTubeUploader(ILogger<FakeYouTubeUploader> logger, IOptions<AppSettings> settings, CancellationTokenSource cts)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.cts = cts ?? throw new ArgumentNullException(nameof(cts));
        }

        private async void FileSystemWatcher_Created(object sender, FileSystemEventArgs e)
        {
            await Task.Factory.StartNew(() => ProcessRecording(e.FullPath), cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Current);
        }

        public async Task ListenAsync()
        {
            await Task.Run(() =>
            {
                using (var recordingWatcher = new FileSystemWatcher(settings.Value.ContextsDirectory, "*.mp4") { EnableRaisingEvents = true, IncludeSubdirectories = true })
                {
                    recordingWatcher.Created += FileSystemWatcher_Created;

                    using (var waiter = new ManualResetEventSlim())
                    {
                        logger.LogInformation("File system watcher listening for mp4 files...");
                        waiter.Wait(cts.Token);
                    }

                    recordingWatcher.Created -= FileSystemWatcher_Created;
                }
            });
        }

        public async Task ProcessRecording(string recordingPath)
        {
            try
            {
                var recording = new FileInfo(recordingPath);
                var entryFile = recording.Directory.GetFiles("*.json", SearchOption.TopDirectoryOnly).FirstOrDefault(file => file.Name.Equals(settings.Value.YouTube.EntryFileName, StringComparison.OrdinalIgnoreCase));

                if (entryFile != null && entryFile.Exists)
                {
                    YouTubeEntry entry = JsonSerializer.Deserialize<YouTubeEntry>(await File.ReadAllTextAsync(entryFile.FullName));
                    logger.LogInformation($"Uploading {recording.Name} as {entry.Title}");
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not upload {recordingPath}");
            }
        }
    }
}
