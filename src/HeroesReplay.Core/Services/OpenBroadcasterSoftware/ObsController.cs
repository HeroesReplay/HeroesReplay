namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware
{
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    using HeroesReplay.Core.Configuration;
    using HeroesReplay.Core.Models;
    using HeroesReplay.Core.Services.Context;
    using HeroesReplay.Core.Services.Shared;
    using Newtonsoft.Json.Linq;

    using OBSWebsocketDotNet;
    using OBSWebsocketDotNet.Types;

    using Polly;

    public class ObsController : IObsController
    {
        private readonly ILogger<ObsController> logger;
        private readonly IReplayContext context;
        private readonly AppSettings settings;
        private readonly OBSWebsocket obs;
        private readonly CancellationTokenProvider tokenProvider;

        public ObsController(ILogger<ObsController> logger, IReplayContext context, AppSettings settings, OBSWebsocket obs, CancellationTokenProvider tokenProvider)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.obs = obs ?? throw new ArgumentNullException(nameof(obs));
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        }

        public void ConfigureFromContext()
        {
            try
            {
                obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
                SetRankImage();
                SetCurrentReplayTextSource();

                if (settings.OBS.RecordingEnabled)
                {
                    obs.SetRecordingFolder(context.Current.Directory.FullName);
                }

                obs.Disconnect();
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not configure OBS from context.");
            }
        }

        private void SetCurrentReplayTextSource()
        {
            try
            {
                List<SourceInfo> sourceList = obs.GetSourcesList();
                SourceInfo replayInfo = sourceList.Find(source => source.Name.Equals(settings.OBS.InfoSourceName));

                if (replayInfo != null)
                {
                    SourceSettings sourceSettings = obs.GetSourceSettings(replayInfo.Name);
                    sourceSettings.sourceSettings["read_from_file"] = true;
                    sourceSettings.sourceSettings["file"] = Path.Combine(context.Current.Directory.FullName, settings.OBS.InfoFileName);
                    obs.SetSourceSettings(replayInfo.Name, sourceSettings.sourceSettings);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not update the Tier for OBS.");
            }
        }

        public void StartRecording()
        {
            Policy
                .Handle<Exception>()
                .WaitAndRetry(retryCount: 5, sleepDurationProvider: (retryAttempt) => TimeSpan.FromSeconds(1))
                .Execute(() =>
                {
                    try
                    {
                        if (settings.OBS.RecordingEnabled)
                        {
                            obs.Connect(settings.OBS.WebSocketEndpoint, password: null);
                            OutputStatus status = obs.GetStreamingStatus();

                            if (!status.IsRecording)
                            {
                                obs.StartRecording();
                            }

                            obs.Disconnect();
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, $"There was an setting the game scene");
                    }
                });
        }

        public void StopRecording()
        {
            Policy
                .Handle<Exception>()
                .WaitAndRetry(retryCount: 5, sleepDurationProvider: (retryAttempt) => TimeSpan.FromSeconds(1))
                .Execute(() =>
                {
                    try
                    {
                        if (settings.OBS.RecordingEnabled)
                        {
                            obs.Connect(settings.OBS.WebSocketEndpoint, password: null);

                            OutputStatus status = obs.GetStreamingStatus();

                            if (status.IsRecording)
                            {
                                obs.StopRecording();
                            }

                            obs.Disconnect();
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, $"There was an setting the game scene");
                    }
                });
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

        /// <summary>
        /// Sets the correct image source [bronze-image, silver-image, gold-image, platinum-image, diamond-image, master-image]
        /// </summary>
        public void SetRankImage()
        {
            if (settings.HeroesProfileApi.EnableMMR)
            {
                try
                {
                    List<SourceInfo> sourceList = obs.GetSourcesList();
                    HideRankImages(sourceList);
                    ShowRankImage(sourceList);

                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Could not update the Tier for OBS.");
                }
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
                        obs.SetCurrentScene(settings.OBS.WaitingSceneName);
                        logger.LogInformation($"Set scene to: {settings.OBS.WaitingSceneName}");
                        obs.Disconnect();
                        return true;
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, $"Could not set scene to {settings.OBS.WaitingSceneName}");
                    }

                    return false;
                });
        }

        public async Task CycleReportAsync()
        {
            if (context.Current.LoadedReplay.ReplayId.HasValue)
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
                        TrySetBrowserSourceSegment(sourceList, segment);
                    }

                    foreach (ReportScene source in settings.OBS.ReportScenes.Where(scene => scene.Enabled))
                    {
                        await TryCycleSceneAsync(source).ConfigureAwait(false);
                    }

                    obs.Disconnect();

                    return true;

                }, tokenProvider.Token);
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

        private bool TrySetBrowserSourceSegment(List<SourceInfo> sourceList, ReportScene segment)
        {
            var url = segment.SourceUrl.ToString().Replace("[ID]", context.Current.LoadedReplay.ReplayId.Value.ToString());
            var source = sourceList.Find(si => si.Name.Equals(segment.SourceName, StringComparison.OrdinalIgnoreCase));

            if (source != null)
            {
                try
                {
                    SourceSettings sourceSettings = obs.GetSourceSettings(source.Name);
                    JObject browserSettings = sourceSettings.sourceSettings;
                    browserSettings["url"] = url;
                    obs.SetSourceSettings(source.Name, browserSettings);
                    return true;
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"could not set {segment.SceneName} URL to: {url}");
                }
            }

            return false;
        }

        private bool ShowRankImage(List<SourceInfo> sourceList)
        {
            if (context.Current.LoadedReplay.HeroesProfileReplay != null)
            {
                if (!string.IsNullOrWhiteSpace(context.Current.LoadedReplay.HeroesProfileReplay.Rank))
                {
                    string rank = context.Current.LoadedReplay.HeroesProfileReplay.Rank.ToLower();

                    SourceInfo imageSource = sourceList.Find(si => si.Name.Equals($"{rank}-image", StringComparison.OrdinalIgnoreCase));

                    if (imageSource != null)
                    {
                        try
                        {
                            obs.SetSourceRender(imageSource.Name, visible: true, sceneName: settings.OBS.GameSceneName);
                            return true;
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e, $"could not set {rank} to visible=false");
                        }
                    }
                    else
                    {
                        logger.LogDebug($"Could not find {rank} image source.");
                    }
                }
            }

            return false;
        }

        private void HideRankImages(List<SourceInfo> sourceList)
        {
            foreach (var rankImageSourceName in settings.OBS.RankImagesSourceNames)
            {
                SourceInfo imageSource = sourceList.Find(sourceInfo => sourceInfo.Name.Equals(rankImageSourceName, StringComparison.OrdinalIgnoreCase));

                if (imageSource != null)
                {
                    try
                    {
                        obs.SetSourceRender(imageSource.Name, visible: false, sceneName: settings.OBS.GameSceneName);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, $"could not set {rankImageSourceName} to visible=false");
                    }
                }
                else
                {
                    logger.LogDebug($"Could not find {rankImageSourceName} image source.");
                }
            }
        }

        private void OnRetry(DelegateResult<bool> wrappedResult, TimeSpan timeSpan)
        {
            if (wrappedResult.Exception != null)
            {
                logger.LogWarning(wrappedResult.Exception, "Could not control OBS");
            }
            else
            {
                logger.LogWarning("Could not control OBS");
            }
        }
    }
}