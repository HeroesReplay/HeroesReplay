using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware
{
    public class ObsEntryMonitor : IObsEntryMonitor
    {
        private readonly ILogger<ObsEntryMonitor> logger;
        private readonly IOptions<AppSettings> settings;
        private readonly IObsController obsController;
        private readonly CancellationTokenSource cts;

        private CancellationTokenSource cancelSessionSource;

        public ObsEntryMonitor(ILogger<ObsEntryMonitor> logger, IOptions<AppSettings> settings, IObsController obsController, CancellationTokenSource cts)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.obsController = obsController ?? throw new ArgumentNullException(nameof(obsController));
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
                        try
                        {
                            logger.LogInformation("File system watcher listening for obs-entry files...");
                            waiter.Wait(cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            logger.LogInformation("File system watcher cancelling...");
                        }
                    }

                    obsEntryWatcher.Created -= FileSystemWatcher_Created;
                }
            });
        }

        private async void FileSystemWatcher_Created(object sender, FileSystemEventArgs e)
        {
            cancelSessionSource?.Cancel();
            using (var sessionTokenSource = new CancellationTokenSource())
            {
                cancelSessionSource = CancellationTokenSource.CreateLinkedTokenSource(sessionTokenSource.Token, cts.Token);
                await Task.Run(() => ControlSession(e.FullPath), cancelSessionSource.Token);
            }
        }

        private bool IsLaunched(ObsEntry obsEntry)
        {
            return Process.GetProcessesByName(settings.Value.Process.HeroesOfTheStorm).Any(process =>
            {
                using (process)
                {
                    return !string.IsNullOrWhiteSpace(process.MainWindowTitle) && process.MainModule.FileVersionInfo.FileVersion == obsEntry.Version;
                }
            });
        }

        private async Task ControlSession(string obsEntryFullPath)
        {
            try
            {
                ObsEntry obsEntry = JsonSerializer.Deserialize<ObsEntry>(await File.ReadAllTextAsync(obsEntryFullPath));

                obsController.SetSession(obsEntry);

                await WaitForLaunch(obsEntry);

                obsController.SwapToGameScene();

                if (settings.Value.Obs.RecordingEnabled)
                {
                    obsController.StartRecording();
                }

                await WaitForExit(obsEntry);

                if (settings.Value.Obs.RecordingEnabled)
                {
                    obsController.StopRecording();
                }

                await obsController.CycleReportAsync();
                obsController.SwapToWaitingScene();
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("The OBS control session has been cancelled.");
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not upload {obsEntryFullPath}");
            }
        }

        private async Task WaitForLaunch(ObsEntry obsEntry)
        {
            while (!IsLaunched(obsEntry))
            {
                logger.LogInformation($"Waiting for {obsEntry.Version}...");
                await Task.Delay(2000);
            }
        }

        private async Task WaitForExit(ObsEntry obsEntry)
        {
            Process process = null;

            try
            {
                while (process == null)
                {
                    process = Process.GetProcessesByName(settings.Value.Process.HeroesOfTheStorm).FirstOrDefault(p => p.MainModule.FileVersionInfo.FileVersion == obsEntry.Version);

                    if (process == null)
                    {
                        await Task.Delay(1000);
                    }
                }

                process.WaitForExit();
            }
            finally
            {
                process?.Dispose();
            }
        }
    }
}
