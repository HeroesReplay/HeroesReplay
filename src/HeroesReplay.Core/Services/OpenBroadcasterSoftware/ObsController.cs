using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Context;
using HeroesReplay.Core.Services.Shared;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using Polly;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

public class ObsController : IObsController
{
    private readonly ILogger<ObsController> logger;
    private readonly IReplayContext context;
    private readonly AppSettings settings;
    private readonly OBSWebsocket obs;
    private readonly CancellationTokenProvider tokenProvider;

    public ObsController(
        ILogger<ObsController> logger,
        IReplayContext context,
        AppSettings settings,
        OBSWebsocket obs,
        CancellationTokenProvider tokenProvider
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.obs = obs ?? throw new ArgumentNullException(nameof(obs));
        this.tokenProvider =
            tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    public void BeginSession()
    {
        ConnectAndWait();
    }

    public void EndSession()
    {
        DisconnectQuietly();
    }

    public void ConfigureFromContext()
    {
        try
        {
            EnsureConnected();
            SetRankImage();
            SetCurrentReplayTextSource();

            if (settings.OBS.RecordingEnabled)
            {
                obs.SetRecordDirectory(context.Current.Directory.FullName);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not configure OBS from context.");
        }
    }

    private void SetCurrentReplayTextSource()
    {
        try
        {
            InputBasicInfo replayInfo = FindInput(settings.OBS.InfoSourceName);

            if (replayInfo != null)
            {
                InputSettings sourceSettings = obs.GetInputSettings(replayInfo.InputName);
                sourceSettings.Settings["read_from_file"] = true;
                sourceSettings.Settings["file"] = Path.Combine(
                    context.Current.Directory.FullName,
                    settings.OBS.InfoFileName
                );
                obs.SetInputSettings(replayInfo.InputName, sourceSettings.Settings);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not update the replay info text source for OBS.");
        }
    }

    public void StartRecording()
    {
        Policy
            .Handle<Exception>()
            .WaitAndRetry(
                retryCount: 5,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(1)
            )
            .Execute(() =>
            {
                try
                {
                    if (settings.OBS.RecordingEnabled)
                    {
                        EnsureConnected();
                        RecordingStatus status = obs.GetRecordStatus();

                        if (!status.IsRecording)
                        {
                            obs.StartRecord();
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "There was an error starting OBS recording.");
                }
            });
    }

    public void StopRecording()
    {
        Policy
            .Handle<Exception>()
            .WaitAndRetry(
                retryCount: 5,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(1)
            )
            .Execute(() =>
            {
                try
                {
                    if (settings.OBS.RecordingEnabled)
                    {
                        EnsureConnected();
                        RecordingStatus status = obs.GetRecordStatus();

                        if (status.IsRecording)
                        {
                            obs.StopRecord();
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "There was an error stopping OBS recording.");
                }
            });
    }

    public void SwapToGameScene()
    {
        Policy
            .Handle<Exception>()
            .OrResult(false)
            .WaitAndRetry(
                retryCount: 5,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(5),
                onRetry: OnRetry
            )
            .Execute(() =>
            {
                try
                {
                    EnsureConnected();
                    obs.SetCurrentProgramScene(settings.OBS.GameSceneName);
                    return true;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "There was an error setting the game scene");
                }

                return false;
            });
    }

    public void SetRankImage()
    {
        if (settings.HeroesProfileApi.EnableMMR)
        {
            try
            {
                HideRankImages();
                ShowRankImage();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not update the Tier for OBS.");
            }
        }
    }

    public void SwapToWaitingScene()
    {
        Policy
            .Handle<Exception>()
            .OrResult(false)
            .WaitAndRetry(
                retryCount: 5,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(5),
                onRetry: OnRetry
            )
            .Execute(() =>
            {
                try
                {
                    EnsureConnected();
                    obs.SetCurrentProgramScene(settings.OBS.WaitingSceneName);
                    logger.LogInformation($"Set scene to: {settings.OBS.WaitingSceneName}");
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
        if (!context.Current.LoadedReplay.ReplayId.HasValue)
        {
            logger.LogInformation(
                "Skipping Heroes Profile report scenes because the replay has no Heroes Profile id."
            );
            return;
        }

        await Policy
            .Handle<Exception>()
            .OrResult(false)
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(5),
                onRetry: OnRetry
            )
            .ExecuteAsync(
                async t =>
                {
                    EnsureConnected();

                    List<InputBasicInfo> sourceList = obs.GetInputList();

                    foreach (
                        ReportScene segment in settings.OBS.ReportScenes.Where(scene =>
                            scene.Enabled
                        )
                    )
                    {
                        TrySetBrowserSourceSegment(sourceList, segment);
                    }

                    foreach (
                        ReportScene source in settings.OBS.ReportScenes.Where(scene =>
                            scene.Enabled
                        )
                    )
                    {
                        await TryCycleSceneAsync(source).ConfigureAwait(false);
                    }

                    return true;
                },
                tokenProvider.Token
            );
    }

    private async Task<bool> TryCycleSceneAsync(ReportScene source)
    {
        try
        {
            obs.SetCurrentProgramScene(source.SceneName);
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

    private bool TrySetBrowserSourceSegment(List<InputBasicInfo> sourceList, ReportScene segment)
    {
        var url = segment
            .SourceUrl.ToString()
            .Replace("[ID]", context.Current.LoadedReplay.ReplayId.Value.ToString());
        var source = sourceList.Find(si =>
            si.InputName.Equals(segment.SourceName, StringComparison.OrdinalIgnoreCase)
        );

        if (source != null)
        {
            try
            {
                InputSettings sourceSettings = obs.GetInputSettings(source.InputName);
                JObject browserSettings = sourceSettings.Settings;
                browserSettings["url"] = url;
                ApplyReportBrowserCss(browserSettings);
                obs.SetInputSettings(source.InputName, browserSettings);
                return true;
            }
            catch (Exception e)
            {
                logger.LogError(e, $"could not set {segment.SceneName} URL to: {url}");
            }
        }

        return false;
    }

    private void ApplyReportBrowserCss(JObject browserSettings)
    {
        string extra = settings.OBS.ReportBrowserCss;
        if (string.IsNullOrWhiteSpace(extra))
        {
            return;
        }

        string current = browserSettings["css"]?.ToString() ?? string.Empty;
        if (current.Contains(extra, StringComparison.Ordinal))
        {
            return;
        }

        browserSettings["css"] = string.IsNullOrWhiteSpace(current)
            ? extra
            : current + "\n" + extra;
    }

    private bool ShowRankImage()
    {
        if (context.Current.LoadedReplay.HeroesProfileReplay != null)
        {
            if (!string.IsNullOrWhiteSpace(context.Current.LoadedReplay.HeroesProfileReplay.Rank))
            {
                string rank = context.Current.LoadedReplay.HeroesProfileReplay.Rank.ToLower();
                string sourceName = $"{rank}-image";

                try
                {
                    SetSceneItemVisible(settings.OBS.GameSceneName, sourceName, visible: true);
                    return true;
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"could not set {rank} to visible=true");
                }
            }
        }

        return false;
    }

    private void HideRankImages()
    {
        foreach (var rankImageSourceName in settings.OBS.RankImagesSourceNames)
        {
            try
            {
                SetSceneItemVisible(
                    settings.OBS.GameSceneName,
                    rankImageSourceName,
                    visible: false
                );
            }
            catch (Exception e)
            {
                logger.LogError(e, $"could not set {rankImageSourceName} to visible=false");
            }
        }
    }

    private void SetSceneItemVisible(string sceneName, string sourceName, bool visible)
    {
        int sceneItemId = obs.GetSceneItemId(sceneName, sourceName, searchOffset: 0);
        obs.SetSceneItemEnabled(sceneName, sceneItemId, visible);
    }

    private InputBasicInfo FindInput(string name)
    {
        return obs.GetInputList()
            .Find(source => source.InputName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureConnected()
    {
        if (!obs.IsIdentified)
        {
            ConnectAndWait();
        }
    }

    private void ConnectAndWait()
    {
        if (obs.IsIdentified)
        {
            return;
        }

        using var identified = new ManualResetEventSlim(false);
        EventHandler handler = (_, _) => identified.Set();
        obs.Connected += handler;

        try
        {
            if (!obs.IsConnected)
            {
                obs.ConnectAsync(
                    settings.OBS.WebSocketEndpoint,
                    settings.OBS.WebSocketPassword ?? string.Empty
                );
            }

            if (obs.IsIdentified)
            {
                return;
            }

            if (!identified.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException(
                    $"OBS websocket at {settings.OBS.WebSocketEndpoint} did not identify in time. "
                        + "OBS Studio 28+ uses obs-websocket 5 on port 4455 (Tools > WebSocket Server Settings)."
                );
            }
        }
        finally
        {
            obs.Connected -= handler;
        }
    }

    private void DisconnectQuietly()
    {
        try
        {
            if (obs.IsConnected)
            {
                obs.Disconnect();
            }
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "OBS disconnect failed.");
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
