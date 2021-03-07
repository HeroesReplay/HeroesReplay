namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware
{
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    using HeroesReplay.Core.Configuration;
    using HeroesReplay.Core.Models;
    using Newtonsoft.Json.Linq;

    using OBSWebsocketDotNet;
    using OBSWebsocketDotNet.Types;

    using Polly;
    using System.Threading;
    using Microsoft.Extensions.Options;

    public class ObsController : IObsController
    {
        private readonly ILogger<ObsController> logger;
        private readonly IOptions<AppSettings> settings;
        private readonly OBSWebsocket obs;
        private readonly CancellationTokenSource cts;
        private readonly ObsOptions obsOptions;

        public ObsController(ILogger<ObsController> logger, IOptions<AppSettings> settings, OBSWebsocket obs, CancellationTokenSource cts)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.obs = obs ?? throw new ArgumentNullException(nameof(obs));
            this.cts = cts ?? throw new ArgumentNullException(nameof(cts));
            this.obsOptions = settings.Value.Obs;
        }

        private void SetCurrentReplayTextSource(ObsEntry obsEntry)
        {
            try
            {
                List<SourceInfo> sourceList = obs.GetSourcesList();
                SourceInfo replayInfo = sourceList.Find(source => source.Name.Equals(obsOptions.InfoSourceName));

                if (replayInfo != null)
                {
                    SourceSettings sourceSettings = obs.GetSourceSettings(replayInfo.Name);
                    sourceSettings.sourceSettings["read_from_file"] = false;

                    // Set the lines
                    sourceSettings.sourceSettings["text"] = string.Join(Environment.NewLine, new[]
                    {
                        !string.IsNullOrWhiteSpace(obsEntry.TwitchLogin) ? $"Requestor: {obsEntry.TwitchLogin}" : string.Empty,
                        !string.IsNullOrWhiteSpace(obsEntry.GameType) ? $"{obsEntry.GameType}" : string.Empty

                    }.Where(line => !string.IsNullOrWhiteSpace(line)));

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
                        if (settings.Value.Obs.RecordingEnabled)
                        {
                            obs.Connect(settings.Value.Obs.WebSocketEndpoint, password: null);
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
                        if (settings.Value.Obs.RecordingEnabled)
                        {
                            obs.Connect(settings.Value.Obs.WebSocketEndpoint, password: null);

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
                .WaitAndRetry(retryCount: 5, sleepDurationProvider: (retryAttempt) => TimeSpan.FromSeconds(5), onRetry: OnRetry)
                .Execute(() =>
                {
                    obs.Connect(obsOptions.WebSocketEndpoint, password: null);
                    obs.SetCurrentScene(obsOptions.GameSceneName);
                    obs.Disconnect();
                });
        }

        /// <summary>
        /// Sets the correct image source [bronze-image, silver-image, gold-image, platinum-image, diamond-image, master-image]
        /// </summary>
        private void SetRankImage(ObsEntry obsEntry)
        {
            if (settings.Value.HeroesProfileApi.EnableMMR)
            {
                List<SourceInfo> sourceList = obs.GetSourcesList();
                HideRankImages(sourceList);
                ShowRankImage(sourceList, obsEntry);
            }
        }

        public void SwapToWaitingScene()
        {
            Policy
                .Handle<Exception>()
                .WaitAndRetry(retryCount: 5, sleepDurationProvider: (retryAttempt) => TimeSpan.FromSeconds(5), onRetry: OnRetry)
                .Execute(() =>
                {
                    obs.Connect(obsOptions.WebSocketEndpoint, password: null);
                    obs.SetCurrentScene(obsOptions.WaitingSceneName);
                    logger.LogInformation($"Set scene to: {obsOptions.WaitingSceneName}");
                    obs.Disconnect();
                });
        }

        public async Task CycleReportAsync()
        {
            await Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: (retryAttempt) => TimeSpan.FromSeconds(5), onRetry: OnRetry)
                .ExecuteAsync(async (t) =>
                {
                    obs.Connect(obsOptions.WebSocketEndpoint, password: null);

                    foreach (ReportScene source in obsOptions.ReportScenes.Where(scene => scene.Enabled))
                    {
                        await CycleSceneAsync(source);
                    }

                    obs.Disconnect();

                }, cts.Token);
        }

        private async Task CycleSceneAsync(ReportScene source)
        {
            obs.SetCurrentScene(source.SceneName);
            logger.LogInformation($"set scene to: {source.SceneName}");
            await Task.Delay(source.DisplayTime).ConfigureAwait(false);
        }

        private void SetBrowserSourceSegment(List<SourceInfo> sourceList, ReportScene segment, ObsEntry entry)
        {
            var url = segment.SourceUrl.ToString().Replace("[ID]", entry.ReplayId);
            var source = sourceList.Find(si => si.Name.Equals(segment.SourceName, StringComparison.OrdinalIgnoreCase));

            if (source != null)
            {
                SourceSettings sourceSettings = obs.GetSourceSettings(source.Name);
                JObject browserSettings = sourceSettings.sourceSettings;
                browserSettings["url"] = url;
                obs.SetSourceSettings(source.Name, browserSettings);
            }
        }

        private void ShowRankImage(List<SourceInfo> sourceList, ObsEntry obsEntry)
        {
            if (!string.IsNullOrWhiteSpace(obsEntry.Rank))
            {
                string rank = obsEntry.Rank.ToLower();

                SourceInfo imageSource = sourceList.Find(si => si.Name.Equals($"{rank}-image", StringComparison.OrdinalIgnoreCase));

                if (imageSource != null)
                {
                    obs.SetSourceRender(imageSource.Name, visible: true, sceneName: obsOptions.GameSceneName);
                }
            }
        }

        private void HideRankImages(List<SourceInfo> sourceList)
        {
            foreach (var rankImageSourceName in obsOptions.RankImagesSourceNames)
            {
                SourceInfo imageSource = sourceList.Find(sourceInfo => sourceInfo.Name.Equals(rankImageSourceName, StringComparison.OrdinalIgnoreCase));

                if (imageSource != null)
                {
                    obs.SetSourceRender(imageSource.Name, visible: false, sceneName: obsOptions.GameSceneName);
                }
            }
        }

        private void OnRetry(Exception exception, TimeSpan timeSpan)
        {
            if (exception != null)
            {
                logger.LogError(exception, "Could not control OBS");
            }
            else
            {
                logger.LogWarning("Could not control OBS");
            }
        }

        public void SetSession(ObsEntry obsEntry)
        {
            try
            {
                obs.Connect(obsOptions.WebSocketEndpoint, password: null);
                SetRankImage(obsEntry);
                SetCurrentReplayTextSource(obsEntry);

                List<SourceInfo> sourceList = obs.GetSourcesList();

                foreach (ReportScene segment in obsOptions.ReportScenes.Where(scene => scene.Enabled))
                {
                    SetBrowserSourceSegment(sourceList, segment, obsEntry);
                }

                if (obsOptions.RecordingEnabled)
                {
                    obs.SetRecordingFolder(obsEntry.RecordingDirectory);
                }

                obs.Disconnect();
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not configure OBS from context.");
            }
        }
    }
}