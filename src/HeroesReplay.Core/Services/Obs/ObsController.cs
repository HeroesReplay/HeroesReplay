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

                EventHandler connected = (sender, e) =>
                {
                    waiter.Set();
                    logger.LogDebug("OBS Web Socket Connected");
                };

                obs.Connected += connected;
                obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
                waiter.Wait();
                obs.Connected -= connected;

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

                EventHandler connected = (sender, e) =>
                {
                    waiter.Set();
                    logger.LogDebug("OBS Web Socket Connected");
                };

                obs.Connected += connected;
                obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
                waiter.Wait();
                obs.Connected -= connected;

                obs.SetCurrentScene(this.settings.OBS.WaitingSceneName);
                logger.LogDebug($"Set scene to: {this.settings.OBS.WaitingSceneName}");

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

                EventHandler connected = (sender, e) =>
                {
                    waiter.Set();
                    logger.LogDebug("OBS Web Socket Connected");
                };

                obs.Connected += connected;
                obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
                waiter.Wait();
                obs.Connected -= connected;

                var sceneList = obs.GetSceneList();
                var sourceList = obs.GetSourcesList();

                foreach (ReportScene? segment in settings.OBS.ReportScenes)
                {
                    TrySetBrowserSourceSegment(replayId, obs, sourceList, segment);
                }

                foreach (ReportScene? source in settings.OBS.ReportScenes)
                {
                    await TryCycleSceneAsync(source);
                }

                obs.Connected -= connected;
                obs.Disconnect();
                logger.LogDebug($"OBS WebSocket Disconnected");
            }
            catch (Exception e)
            {
                logger.LogError(e, $"There was an error cycling the interim panels");
            }
        }

        private async Task<bool> TryCycleSceneAsync(ReportScene source)
        {
            try
            {
                obs.SetCurrentScene(source.SceneName);
                logger.LogDebug($"set scene to: {source.SceneName}");
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

        public async Task UpdateMMRTierAsync(string text)
        {
            text = string.IsNullOrEmpty(text) ? string.Empty : text;
            text = text.Trim().ToLower();

            string? tier = null;
            string? division = null;

            try
            {
                tier = text.Split(' ')[0].Trim();
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not extract Tier from {text}");
            }

            try
            {
                division = text.Split(' ')[1].Trim();
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not extract division from {text}");
            }

            var waiter = new ManualResetEventSlim();

            EventHandler connected = (sender, e) =>
            {
                waiter.Set();
                logger.LogDebug("OBS Web Socket Connected");
            };

            obs.Connected += connected;
            obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
            waiter.Wait();
            obs.Connected -= connected;

            var sourceList = obs.GetSourcesList();

            // HIDE ALL
            foreach (var tierSource in this.settings.OBS.TierSources)
            {
                var imageSource = sourceList.Find(si => si.Name.Equals(tierSource, StringComparison.OrdinalIgnoreCase));

                if (imageSource != null)
                {
                    try
                    {
                        obs.SetSourceRender(imageSource.Name, false);
                        logger.LogDebug($"set {imageSource.Name} to visible=false");
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, $"could not set {tierSource} to visible=false");
                    }
                }
            }

            try
            {
                // SHOW CORRECT TIER
                var source = sourceList.Find(si => si.Name.StartsWith(tier, StringComparison.OrdinalIgnoreCase));

                if (source != null)
                {
                    try
                    {
                        obs.SetSourceRender(source.Name, true);
                        logger.LogDebug($"set {source.Name} to visible=true");
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, $"could not set {tier} to visible=false");
                    }
                }

                if (division != null)
                {
                    var divisionSource = sourceList.Find(si => si.Name.Equals(settings.OBS.TierTextSourceName, StringComparison.OrdinalIgnoreCase));

                    if (divisionSource != null)
                    {
                        try
                        {
                            // We dont want to set something that IS NOT 1-5
                            if (int.TryParse(division, out int result) && result >= 1 || result <= 5)
                            {
                                obs.SetTextGDIPlusProperties(new TextGDIPlusProperties { SourceName = source.Name, Text = division });
                                obs.SetSourceRender(divisionSource.Name, true);
                                logger.LogDebug($"set {source.Name} to visible=true");
                            }
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e, $"could not set {tier} to visible=false");
                        }
                    }
                    else
                    {
                        obs.SetTextGDIPlusProperties(new TextGDIPlusProperties { SourceName = source.Name, Text = string.Empty });
                        obs.SetSourceRender(divisionSource.Name, false);
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not set the Tier from {text} for OBS");
            }

            obs.Disconnect();
        }
    }
}
