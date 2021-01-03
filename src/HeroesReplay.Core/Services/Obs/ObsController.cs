using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json.Linq;

using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using System.Linq;

namespace HeroesReplay.Core.Services.Obs
{
    public class ObsController : IObsController
    {
        private readonly ILogger<ObsController> logger;
        private readonly AppSettings settings;
        private readonly OBSWebsocket obs;

        public ObsController(ILogger<ObsController> logger, AppSettings settings, OBSWebsocket obs)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.obs = obs ?? throw new ArgumentNullException(nameof(obs));
        }

        public void SwapToGameScene()
        {
            try
            {
                var waiter = new ManualResetEventSlim();

                void connected(object sender, EventArgs e)
                {
                    waiter.Set();
                    logger.LogDebug("OBS Web Socket Connected");
                }

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

        public void SwapToWaitingScene()
        {
            try
            {
                var waiter = new ManualResetEventSlim();

                void connected(object sender, EventArgs e)
                {
                    waiter.Set();
                    logger.LogInformation("OBS Web Socket Connected");
                }

                obs.Connected += connected;
                obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
                waiter.Wait();
                obs.Connected -= connected;

                obs.SetCurrentScene(this.settings.OBS.WaitingSceneName);
                logger.LogInformation($"Set scene to: {this.settings.OBS.WaitingSceneName}");

                obs.Disconnect();
                logger.LogInformation($"OBS WebSocket Disconnected");
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

                void connected(object sender, EventArgs e)
                {
                    waiter.Set();
                    logger.LogInformation("OBS Web Socket Connected");
                }

                obs.Connected += connected;
                obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
                waiter.Wait();
                obs.Connected -= connected;

                var sceneList = obs.GetSceneList();
                var sourceList = obs.GetSourcesList();

                foreach (ReportScene segment in settings.OBS.ReportScenes.Where(scene => scene.Enabled))
                {
                    TrySetBrowserSourceSegment(replayId, obs, sourceList, segment);
                }

                foreach (ReportScene source in settings.OBS.ReportScenes.Where(scene => scene.Enabled))
                {
                    await TryCycleSceneAsync(source).ConfigureAwait(false);
                }

                obs.Connected -= connected;
                obs.Disconnect();
                logger.LogInformation($"OBS WebSocket Disconnected");
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
                logger.LogInformation($"set scene to: {source.SceneName}");
                await Task.Delay(source.DisplayTime).ConfigureAwait(false);
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
            var url = segment.SourceUrl.ToString().Replace("[ID]", replayId.ToString());
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

        public void UpdateMMRTier((int RankPoints, string Tier) mmr)
        {
            var text = string.IsNullOrEmpty(mmr.Tier) ? string.Empty : mmr.Tier;
            text = text.Trim().ToLower();

            string tier = null;
            string division = null;

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

            try
            {
                var waiter = new ManualResetEventSlim();

                void connected(object sender, EventArgs e)
                {
                    waiter.Set();
                    logger.LogDebug("OBS Web Socket Connected");
                }

                obs.Connected += connected;
                obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
                waiter.Wait();
                obs.Connected -= connected;

                var sourceList = obs.GetSourcesList();

                try
                {
                    obs.SetSourceRender(settings.OBS.TierDivisionSourceName, visible: false, sceneName: settings.OBS.GameSceneName);
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Could not set {settings.OBS.TierDivisionSourceName} to visible=false");
                }

                try
                {
                    obs.SetSourceRender(settings.OBS.TierRankPointsSourceName, visible: false, sceneName: settings.OBS.GameSceneName);
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Could not set {settings.OBS.TierRankPointsSourceName} to visible=false");
                }

                foreach (var tierSourceName in settings.OBS.TierSources)
                {
                    SourceInfo imageSource = sourceList.Find(sourceInfo => sourceInfo.Name.Equals(tierSourceName, StringComparison.OrdinalIgnoreCase));

                    if (imageSource != null)
                    {
                        try
                        {
                            obs.SetSourceRender(imageSource.Name, visible: false, sceneName: settings.OBS.GameSceneName);
                            logger.LogDebug($"set {imageSource.Name} to visible=false");
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e, $"could not set {tierSourceName} to visible=false");
                        }
                    }
                    else
                    {
                        logger.LogDebug($"Could not find {tierSourceName} image source.");
                    }
                }

                try
                {
                    SourceInfo imageSource = sourceList.Find(si => si.Name.Equals($"{tier}-image", StringComparison.OrdinalIgnoreCase));

                    if (imageSource != null)
                    {
                        try
                        {
                            obs.SetSourceRender(imageSource.Name, visible: true, sceneName: settings.OBS.GameSceneName);
                            logger.LogInformation($"set {imageSource.Name} to visible=true");
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e, $"could not set {tier} to visible=false");
                        }
                    }
                    else
                    {
                        logger.LogDebug($"Could not find {tier} image source.");
                    }

                    if (division != null)
                    {
                        SourceInfo divisionSource = sourceList.Find(si => si.Name.Equals(settings.OBS.TierDivisionSourceName, StringComparison.OrdinalIgnoreCase));

                        if (divisionSource != null)
                        {
                            try
                            {
                                bool isValidDivision = int.TryParse(division, out int result) && result >= 1 || result <= 5;

                                if (isValidDivision)
                                {
                                    var properties = obs.GetTextGDIPlusProperties(divisionSource.Name);
                                    properties.Text = division;
                                    obs.SetTextGDIPlusProperties(properties);
                                    obs.SetSourceRender(divisionSource.Name, visible: true, sceneName: settings.OBS.GameSceneName);
                                    logger.LogInformation($"set {divisionSource.Name} to visible=true");
                                }
                            }
                            catch (Exception e)
                            {
                                logger.LogError(e, $"could not set {tier} to visible=false");
                            }
                        }
                        else
                        {
                            logger.LogDebug($"could not find source {settings.OBS.TierDivisionSourceName}");
                        }
                    }
                    else
                    {
                        SourceInfo rankPointsSource = sourceList.Find(si => si.Name.Equals(settings.OBS.TierRankPointsSourceName, StringComparison.OrdinalIgnoreCase));

                        if (rankPointsSource != null)
                        {
                            try
                            {
                                var properties = obs.GetTextGDIPlusProperties(rankPointsSource.Name);
                                properties.Text = mmr.RankPoints.ToString();
                                obs.SetTextGDIPlusProperties(properties);
                                obs.SetSourceRender(rankPointsSource.Name, visible: true, sceneName: settings.OBS.GameSceneName);
                                logger.LogInformation($"set {rankPointsSource.Name} to visible=true");
                            }
                            catch (Exception e)
                            {
                                logger.LogError(e, $"could not set {tier} to visible=false");
                            }
                        }
                        else
                        {
                            logger.LogDebug($"could not find source {settings.OBS.TierRankPointsSourceName}");
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Could not set the Tier from {text} for OBS.");
                }

                obs.Disconnect();
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not set the Tier from {text} for OBS.");
            }
        }
    }
}
