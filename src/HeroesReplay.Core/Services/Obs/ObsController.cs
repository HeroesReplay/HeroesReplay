using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json.Linq;

using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using System.Linq;
using Polly;
using HeroesReplay.Core.Shared;
using System.IO;
using HeroesReplay.Core.Services.HeroesProfile;

namespace HeroesReplay.Core.Services.Obs
{
    public class ObsController : IObsController
    {
        private readonly ILogger<ObsController> logger;
        private readonly AppSettings settings;
        private readonly OBSWebsocket obs;
        private readonly CancellationTokenProvider tokenProvider;

        public ObsController(ILogger<ObsController> logger, AppSettings settings, OBSWebsocket obs, CancellationTokenProvider tokenProvider)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.obs = obs ?? throw new ArgumentNullException(nameof(obs));
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        }

        public void Configure()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(settings.OBS.RecordingFolderDirectory))
                {
                    DirectoryInfo directory = Directory.CreateDirectory(settings.OBS.RecordingFolderDirectory);
                    obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
                    obs.SetRecordingFolder(directory.FullName);
                    obs.Disconnect();
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not configure OBS");
            }
        }

        public void SwapToGameScene()
        {
            Policy
                .Handle<Exception>()
                .OrResult(false)
                .WaitAndRetry(retryCount: 5, sleepDurationProvider: (retryAttempt) => TimeSpan.FromSeconds(5), onRetry: OnRetry)
                .Execute(() =>
                {
                    try
                    {
                        obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
                        obs.SetCurrentScene(settings.OBS.GameSceneName);

                        if (settings.OBS.RecordSessionEnabled)
                        {
                            obs.StartRecording();
                        }

                        obs.Disconnect();
                        return true;
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, $"There was an setting the game scene");
                    }

                    return false;
                });
        }

        public void UpdateMMRTier(ReplayData replayData)
        {
            var (tier, division) = GetTierAndDivision(replayData);

            try
            {
                obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
                var sourceList = obs.GetSourcesList();

                TryHideDivision();
                TryHideRatingPoints();
                TryHideTierImage(sourceList);

                try
                {
                    TryShowTierImage(tier, sourceList);

                    if (TrySetDivision(tier, division, sourceList))
                    {
                        // Division 1 - 5 set, so we dont need to write the actual MMR value (1500 - 3000~)
                    }
                    else
                    {
                        TrySetAverageRating(replayData.AverageRating, tier, sourceList);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Could not set the Tier from {tier} for OBS.");
                }

                obs.Disconnect();
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not update the Tier for OBS.");
            }
        }

        public void SwapToWaitingScene()
        {
            Policy
                .Handle<Exception>()
                .OrResult(false)
                .WaitAndRetry(retryCount: 5, sleepDurationProvider: (retryAttempt) => TimeSpan.FromSeconds(5), onRetry: OnRetry)
                .Execute(() =>
                {
                    try
                    {
                        obs.Connect(settings.OBS.WebSocketEndpoint, password: null);

                        // Stop recording before we trigger the scene with the Blizzard Soundcloud music
                        if (settings.OBS.RecordSessionEnabled)
                        {
                            obs.StopRecording();
                        }

                        obs.SetCurrentScene(settings.OBS.WaitingSceneName);
                        logger.LogInformation($"Set scene to: {settings.OBS.WaitingSceneName}");
                        obs.Disconnect();
                        logger.LogInformation($"OBS WebSocket Disconnected");
                        return true;
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, $"There was an setting the game scene");
                    }

                    return false;
                });
        }

        public async Task CycleReportAsync(int replayId)
        {
            await Policy
                .Handle<Exception>()
                .OrResult(false)
                .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: (retryAttempt) => TimeSpan.FromSeconds(5), onRetry: OnRetry)
                .ExecuteAsync(async (t) =>
                {
                    obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
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

                    obs.Disconnect();

                    return true;

                }, tokenProvider.Token);
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

        private (string tier, int division) GetTierAndDivision(ReplayData replayData)
        {
            string text = (string.IsNullOrEmpty(replayData.Tier) ? string.Empty : replayData.Tier).Trim().ToLower();

            string tier = string.Empty;
            int division = 0;

            try
            {
                tier = text.Split(' ')[0].Trim();
            }
            catch (Exception)
            {
                logger.LogInformation($"Could not extract Tier from {text}");
            }

            try
            {
                division = int.Parse(text.Split(' ')[1].Trim());
            }
            catch (Exception)
            {
                logger.LogInformation($"Could not extract division from {text}");
            }

            return (tier, division);
        }

        private void TryHideDivision()
        {
            try
            {
                obs.SetSourceRender(settings.OBS.TierDivisionSourceName, visible: false, sceneName: settings.OBS.GameSceneName);
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not set {settings.OBS.TierDivisionSourceName} to visible=false");
            }
        }

        private bool TrySetDivision(string tier, int division, List<SourceInfo> sourceList)
        {
            SourceInfo divisionSource = sourceList.Find(si => si.Name.Equals(settings.OBS.TierDivisionSourceName, StringComparison.OrdinalIgnoreCase));

            if (divisionSource != null)
            {
                try
                {
                    bool isValidDivision = division >= 1 && division <= 5;

                    if (isValidDivision)
                    {
                        var properties = obs.GetTextGDIPlusProperties(divisionSource.Name);
                        properties.Text = division.ToString();
                        obs.SetTextGDIPlusProperties(properties);
                        obs.SetSourceRender(divisionSource.Name, visible: true, sceneName: settings.OBS.GameSceneName);
                        logger.LogInformation($"set {divisionSource.Name} to visible=true");
                        return true;
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

            return false;
        }

        private bool TrySetAverageRating(int average, string tier, List<SourceInfo> sourceList)
        {
            SourceInfo rankPointsSource = sourceList.Find(si => si.Name.Equals(settings.OBS.TierRankPointsSourceName, StringComparison.OrdinalIgnoreCase));

            if (rankPointsSource != null)
            {
                try
                {
                    var properties = obs.GetTextGDIPlusProperties(rankPointsSource.Name);
                    properties.Text = average.ToString();
                    obs.SetTextGDIPlusProperties(properties);
                    obs.SetSourceRender(rankPointsSource.Name, visible: true, sceneName: settings.OBS.GameSceneName);
                    logger.LogInformation($"set {rankPointsSource.Name} to visible=true");

                    return true;
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"could not set {average} points for {tier}.");
                }
            }
            else
            {
                logger.LogDebug($"could not find source {settings.OBS.TierRankPointsSourceName}");
            }

            return false;
        }

        private bool TryShowTierImage(string tier, List<SourceInfo> sourceList)
        {
            SourceInfo imageSource = sourceList.Find(si => si.Name.Equals($"{tier}-image", StringComparison.OrdinalIgnoreCase));

            if (imageSource != null)
            {
                try
                {
                    obs.SetSourceRender(imageSource.Name, visible: true, sceneName: settings.OBS.GameSceneName);
                    logger.LogInformation($"set {imageSource.Name} to visible=true");
                    return true;
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

            return false;
        }

        private bool TryHideTierImage(List<SourceInfo> sourceList)
        {
            foreach (var tierSourceName in settings.OBS.TierSources)
            {
                SourceInfo imageSource = sourceList.Find(sourceInfo => sourceInfo.Name.Equals(tierSourceName, StringComparison.OrdinalIgnoreCase));

                if (imageSource != null)
                {
                    try
                    {
                        obs.SetSourceRender(imageSource.Name, visible: false, sceneName: settings.OBS.GameSceneName);
                        logger.LogDebug($"set {imageSource.Name} to visible=false");
                        return true;
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

            return false;
        }

        private bool TryHideRatingPoints()
        {
            try
            {
                obs.SetSourceRender(settings.OBS.TierRankPointsSourceName, visible: false, sceneName: settings.OBS.GameSceneName);
                return true;
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not set {settings.OBS.TierRankPointsSourceName} to visible=false");
            }

            return false;
        }

        private void OnRetry(DelegateResult<bool> wrappedResult, TimeSpan timeSpan)
        {
            logger.LogWarning("Could not control OBS");
        }
    }
}