using System.IO;
using System.Linq;
using System.Threading.Tasks;

using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware
{
    public class FakeObsController : IObsController
    {
        private readonly ILogger<FakeObsController> logger;
        private ObsEntry obsEntry;

        public FakeObsController(ILogger<FakeObsController> logger)
        {
            this.logger = logger;
        }

        public Task CycleReportAsync()
        {
            logger.LogInformation("Cycled report-scenes.");
            return Task.CompletedTask;
        }

        public void SetSession(ObsEntry obsEntry)
        {
            logger.LogInformation($"Set obs session for: {obsEntry.ReplayId}");
            this.obsEntry = obsEntry;
        }

        public void StartRecording() => logger.LogInformation("Started recording");

        public void StopRecording()
        {
            byte[] fakeData = Enumerable.Range(0, 100).Select(x => byte.MaxValue).OfType<byte>().ToArray();
            File.WriteAllBytes(Path.Combine(obsEntry.RecordingDirectory, "fake.mp4"), fakeData);
        }

        public void SwapToGameScene()
        {
            logger.LogInformation("Swapped to game-scene.");
        }

        public void SwapToWaitingScene()
        {
            logger.LogInformation("Swapped to waiting-scene.");
        }
    }
}
