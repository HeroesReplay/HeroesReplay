using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using Newtonsoft.Json.Linq;

using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Obs
{
    public class ObsController : IObsController
    {
        private readonly ILogger<ObsController> logger;
        private readonly Settings settings;
        private readonly OBSWebsocket obs;

        public ObsController(ILogger<ObsController> logger, Settings settings, OBSWebsocket obs)
        {
            this.logger = logger;
            this.settings = settings;
            this.obs = obs;
        }

        public async Task SwapToGameSceneAsync()
        {
            try
            {
                var waiter = new ManualResetEventSlim();

                obs.Connected += (sender, e) =>
                {
                    waiter.Set();
                    logger.LogDebug("OBS Web Socket Connected");
                };

                obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
                waiter.Wait();

                obs.SetCurrentScene(this.settings.OBS.GameSceneName);

                obs.Disconnect();
                logger.LogDebug($"OBS WebSocket Disconnected");
            }
            catch (Exception e)
            {
                logger.LogError(e, $"There was an setting the game scene");
            }
        }

        public async Task SwapToWaitingSceneAsync()
        {
            try
            {
                var waiter = new ManualResetEventSlim();

                obs.Connected += (sender, e) =>
                {
                    waiter.Set();
                    logger.LogDebug("OBS Web Socket Connected");
                };

                obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
                waiter.Wait();

                obs.SetCurrentScene(this.settings.OBS.WaitingSceneName);

                obs.Disconnect();
                logger.LogDebug($"OBS WebSocket Disconnected");
            }
            catch (Exception e)
            {
                logger.LogError(e, $"There was an setting the game scene");
            }
        }

        public async Task CycleReportAsync(int replayId)
        {
            try
            {
                var waiter = new ManualResetEventSlim();

                obs.Connected += (sender, e) =>
                {
                    waiter.Set();
                    logger.LogDebug("OBS Web Socket Connected");
                };

                obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
                waiter.Wait();

                var sceneList = obs.GetSceneList();
                var sourceList = obs.GetSourcesList();

                foreach (ReportScene? segment in this.settings.OBS.ReportScenes)
                {
                    TrySetBrowserSourceSegment(replayId, obs, sourceList, segment);
                }

                foreach (ReportScene? source in this.settings.OBS.ReportScenes)
                {
                    await TryCycleScene(source);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"There was an error cycling the interim panels");
            }
        }

        private async Task<bool> TryCycleScene(ReportScene source)
        {
            try
            {
                logger.LogDebug($"set scene to: {source.SceneName}");
                obs.SetCurrentScene(source.SceneName);
                await Task.Delay(source.DisplayTime);
                return true;
            }
            catch (Exception e)
            {
                logger.LogError(e, $"could not set scene to {source.SceneName}");
            }

            return false;
        }

        private bool TrySetBrowserSourceSegment(int replayId, OBSWebsocket obs, List<SourceInfo> sourceList, ReportScene segment)
        {
            var url = segment.SourceUrl.Replace("[ID]", replayId.ToString());
            var source = sourceList.Find(si => si.Name.Equals(segment.SourceName, StringComparison.OrdinalIgnoreCase));

            if (source != null)
            {
                try
                {
                    SourceSettings sourceSettings = obs.GetSourceSettings(source.Name);
                    JObject browserSettings = sourceSettings.sourceSettings;
                    browserSettings["url"] = url;
                    obs.SetSourceSettings(source.Name, browserSettings);
                    logger.LogDebug($"set {segment.SceneName} URL to: {url}");
                    return true;
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"could not set {segment.SceneName} URL to: {url}");
                }
            }

            return false;
        }
    }
}
