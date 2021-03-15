using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core.Services;

using Microsoft.Extensions.Logging;

using OBSWebsocketDotNet;

namespace HeroesReplay.Service.Obs.Core
{
    public class ObsService : BackgroundService
    {
        private readonly ILogger<ObsService> logger;
        private readonly IObsEntryMonitor obsEntryMonitor;

        public ObsService(ILogger<ObsService> logger, IObsEntryMonitor obsEntryMonitor, CancellationTokenSource cts) : base(cts)
        {
            this.logger = logger;
            this.obsEntryMonitor = obsEntryMonitor;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await obsEntryMonitor.ListenAsync();
            }
        }
    }

    public interface IObsHealthChecker
    {
        Task RestartOnCrashDetectionAsync();
    }

    public class ObsHealthChecker : IObsHealthChecker
    {
        private readonly OBSWebsocket obsWebsocket;
        private readonly CancellationTokenSource cts;

        public ObsHealthChecker(OBSWebsocket obsWebsocket, CancellationTokenSource cts)
        {
            this.obsWebsocket = obsWebsocket;
            this.cts = cts;
        }

        public async Task RestartOnCrashDetectionAsync()
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    this.obsWebsocket.Connect("ws://localhost", null);
                    var previous = this.obsWebsocket.GetStats();

                    await Task.Delay(TimeSpan.FromSeconds(10));

                    var next = this.obsWebsocket.GetStats();

                    var crashed = previous.RenderTotalFrames == next.RenderTotalFrames;

                    if (crashed)
                    {
                        obsWebsocket.Disconnect();

                        foreach (var obsStudio in Process.GetProcessesByName("OBS Studio"))
                        {
                            using (obsStudio)
                            {
                                obsStudio.Kill(true);
                            }
                        }

                        using (var process = Process.Start("Obs.exe"))
                        {
                            process.WaitForInputIdle();
                        }

                        obsWebsocket.Connect("ws://localhost", null);
                        obsWebsocket.StartStreaming();
                        obsWebsocket.Disconnect();
                    }
                }
                catch (Exception)
                {

                }

                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        }
    }
}
