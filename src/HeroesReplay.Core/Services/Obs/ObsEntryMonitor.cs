using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware
{
    /// <summary>
    /// Listens for obs-entry.json files so it knows:
    /// 1. When to swap the game process
    /// 2. Configure obs overlay text/images
    /// 3. The report summary information to show at the end
    /// 4. Going back to the waiting screen until the next obs-entry.json file is picked up
    /// </summary>
    public class ObsEntryMonitor : IObsEntryMonitor
    {
        private readonly ILogger<ObsEntryMonitor> logger;
        private readonly IOptions<AppSettings> settings;
        private readonly IObsController obsController;
        private readonly CancellationTokenSource cts;

        public ObsEntryMonitor(ILogger<ObsEntryMonitor> logger, IOptions<AppSettings> settings, IObsController obsController, CancellationTokenSource cts)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.obsController = obsController;
            this.cts = cts ?? throw new ArgumentNullException(nameof(cts));
        }

        public async Task ListenAsync()
        {
            await Task.Run(() =>
            {
                using (var obsEntryWatcher = new FileSystemWatcher(settings.Value.ContextsDirectory, settings.Value.Obs.EntryFileName) { EnableRaisingEvents = true, IncludeSubdirectories = true })
                {
                    obsEntryWatcher.Created += FileSystemWatcher_Created;

                    using (var waiter = new ManualResetEventSlim())
                    {
                        logger.LogInformation("File system watcher listening for obs-entry files...");
                        waiter.Wait(cts.Token);
                    }

                    obsEntryWatcher.Created -= FileSystemWatcher_Created;
                }
            });
        }

        private async void FileSystemWatcher_Created(object sender, FileSystemEventArgs e)
        {
            await Task.Factory.StartNew(() => ControlSession(e.FullPath), cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Current);
        }

        private async Task ControlSession(string obsEntryFullPath)
        {
            try
            {
                ObsEntry obsEntry = JsonSerializer.Deserialize<ObsEntry>(await File.ReadAllTextAsync(obsEntryFullPath));

                // 1 Set Session
                this.obsController.SetSession(obsEntry);

                obsController.SwapToGameScene();
                obsController.StartRecording();

                // 3 Cycle Report
                await obsController.CycleReportAsync();

                // 4 Loading Screen
                obsController.SwapToWaitingScene();
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not upload {obsEntryFullPath}");
            }
        }
    }
}
