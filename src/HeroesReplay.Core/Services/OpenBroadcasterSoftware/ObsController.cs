using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            if (ShouldRecord())
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
                string path = Path.Combine(
                    context.Current.Directory.FullName,
                    settings.OBS.InfoFileName
                );
                sourceSettings.Settings["read_from_file"] = true;
                sourceSettings.Settings["file"] = path;
                obs.SetInputSettings(replayInfo.InputName, sourceSettings.Settings);
                bool show = File.Exists(path) && File.ReadAllText(path).Trim().Length > 0;
                SetSceneItemVisible(settings.OBS.GameSceneName, replayInfo.InputName, show);
                logger.LogInformation(
                    show
                        ? "OBS replay details {Source} visible."
                        : "OBS replay details {Source} hidden.",
                    replayInfo.InputName
                );
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
                    if (ShouldRecord())
                    {
                        EnsureConnected();
                        RecordingStatus status = obs.GetRecordStatus();

                        if (!status.IsRecording)
                        {
                            logger.LogInformation(
                                "Starting OBS recording for replay {ReplayId} ({Reason}). Stops when spectate ends.",
                                context.Current?.LoadedReplay?.ReplayId,
                                SessionMedia.HasRequestor(context.Current?.LoadedReplay)
                                    ? "viewer request"
                                    : "every replay"
                            );
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
                    EnsureConnected();
                    RecordingStatus status = obs.GetRecordStatus();
                    if (status.IsRecording)
                    {
                        logger.LogInformation(
                            "Stopping OBS recording for replay {ReplayId}.",
                            context.Current?.LoadedReplay?.ReplayId
                        );
                        obs.StopRecord();
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "There was an error stopping OBS recording.");
                }
            });
    }

    public TimeSpan? RecordingElapsed()
    {
        try
        {
            EnsureConnected();
            RecordingStatus status = obs.GetRecordStatus();
            if (!status.IsRecording || status.RecordingDuration < 0)
            {
                return null;
            }

            return TimeSpan.FromMilliseconds(status.RecordingDuration);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "OBS recording time is not available.");
            return null;
        }
    }

    public void StartStreaming()
    {
        if (!SessionMedia.ShouldStream(settings.OBS))
        {
            logger.LogDebug("Skipping OBS StartStream because OBS:StreamingEnabled is false.");
            return;
        }

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
                    EnsureConnected();
                    OutputStatus status = obs.GetStreamStatus();
                    if (!status.IsActive)
                    {
                        logger.LogInformation("Starting OBS stream.");
                        obs.StartStream();
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "There was an error starting OBS streaming.");
                }
            });
    }

    public void StopStreaming()
    {
        if (!SessionMedia.ShouldStream(settings.OBS))
        {
            logger.LogDebug("Skipping OBS StopStream because OBS:StreamingEnabled is false.");
            return;
        }

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
                    EnsureConnected();
                    OutputStatus status = obs.GetStreamStatus();
                    if (status.IsActive)
                    {
                        logger.LogInformation("Stopping OBS stream.");
                        obs.StopStream();
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "There was an error stopping OBS streaming.");
                }
            });
    }

    public bool IsStreaming()
    {
        try
        {
            if (!obs.IsIdentified)
            {
                return false;
            }

            return obs.GetStreamStatus().IsActive;
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not read OBS stream status.");
            return false;
        }
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
                    logger.LogInformation("Set scene to: {Scene}", settings.OBS.GameSceneName);
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
        try
        {
            HideRankImages();
            HideRankCaption();
            ShowRankImage();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not update the rank image for OBS.");
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
                            scene.Enabled && !IsMissingLocalFile(scene)
                        )
                    )
                    {
                        TrySetBrowserSourceSegment(sourceList, segment);
                    }

                    foreach (
                        ReportScene source in settings.OBS.ReportScenes.Where(scene =>
                            scene.Enabled && !IsMissingLocalFile(scene)
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

    private bool IsMissingLocalFile(ReportScene scene)
    {
        if (
            scene?.SourceUrl == null
            || !scene.SourceUrl.IsAbsoluteUri
            || scene.SourceUrl.Scheme != Uri.UriSchemeFile
        )
        {
            return false;
        }

        if (File.Exists(scene.SourceUrl.LocalPath))
        {
            return false;
        }

        logger.LogInformation(
            "Skipping report scene {Scene} because {Path} does not exist yet.",
            scene.SceneName,
            scene.SourceUrl.LocalPath
        );
        return true;
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
                SetMatchReportScroll(segment);
                return true;
            }
            catch (Exception e)
            {
                logger.LogError(e, $"could not set {segment.SceneName} URL to: {url}");
            }
        }

        return false;
    }

    private void SetMatchReportScroll(ReportScene segment)
    {
        if (!string.Equals(segment.SceneName, "match-report", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        double speed = MatchReportPace.ScrollSpeedY(segment.DisplayTime);
        try
        {
            obs.SetSourceFilterSettings(
                segment.SourceName,
                "Scroll",
                new JObject { ["speed_y"] = speed },
                overlay: true
            );
            logger.LogInformation(
                "Match report scroll speed {Speed} for {Duration}.",
                speed,
                segment.DisplayTime
            );
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not set the match report scroll speed.");
        }
    }

    private void ApplyReportBrowserCss(JObject browserSettings)
    {
        string extra =
            (settings.OBS.ReportBrowserCss ?? string.Empty)
            + " a[href*='/Match/Single/'],a[href*='replayID=']{font-size:0!important;color:transparent!important;pointer-events:none!important;}";
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
        HeroesProfileReplay row = context.Current?.LoadedReplay?.HeroesProfileReplay;
        string rank = row?.Rank;
        int? leagueTier = row?.LeagueTier;
        if (string.IsNullOrWhiteSpace(RankImage.SourceName(rank, leagueTier)))
        {
            rank = RankImage.RankFromCacheFileName(context.Current?.LoadedReplay?.FileInfo?.Name);
            leagueTier = null;
        }

        string sourceName = RankImage.SourceName(rank, leagueTier);
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            logger.LogInformation(
                "No rank badge for replay {ReplayId} ({File}).",
                row?.Id,
                context.Current?.LoadedReplay?.FileInfo?.Name
            );
            return false;
        }

        try
        {
            SetSceneItemVisible(settings.OBS.GameSceneName, sourceName, visible: true);
            logger.LogInformation("OBS rank image {Source} visible.", sourceName);
            ShowRankCaption(rank, row?.AverageMmr);
            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not show rank image {Source}.", sourceName);
            return false;
        }
    }

    private void ShowRankCaption(string rank, double? mmr)
    {
        HideRankCaption();
        string division = RankImage.Division(rank);
        if (division != null)
        {
            SetText(settings.OBS.TierDivisionSourceName, division, visible: true);
            logger.LogInformation("OBS tier division {Division} visible.", division);
            return;
        }

        string points = RankImage.PointsText(mmr);
        if (points != null)
        {
            SetText(settings.OBS.TierRankPointsSourceName, points, visible: true);
            logger.LogInformation("OBS rank points {Points} visible.", points);
        }
    }

    private void HideRankCaption()
    {
        HideSceneItem(settings.OBS.TierDivisionSourceName);
        HideSceneItem(settings.OBS.TierRankPointsSourceName);
    }

    private void HideSceneItem(string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return;
        }

        try
        {
            SetSceneItemVisible(settings.OBS.GameSceneName, sourceName, visible: false);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not hide {Source}.", sourceName);
        }
    }

    private void HideRankImages()
    {
        IEnumerable<string> names = settings.OBS.RankImagesSourceNames ?? RankImage.SourceNames;
        foreach (string rankImageSourceName in names)
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
                logger.LogDebug(e, "Could not hide {Source}.", rankImageSourceName);
            }
        }
    }

    private void SetText(string sourceName, string text, bool visible)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return;
        }

        InputBasicInfo input = FindInput(sourceName);
        if (input == null)
        {
            logger.LogDebug("OBS source {Source} was not found.", sourceName);
            return;
        }

        InputSettings sourceSettings = obs.GetInputSettings(input.InputName);
        sourceSettings.Settings["read_from_file"] = false;
        sourceSettings.Settings["text"] = text;
        obs.SetInputSettings(input.InputName, sourceSettings.Settings);
        SetSceneItemVisible(settings.OBS.GameSceneName, input.InputName, visible);
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

    private void EnsureObsProcess()
    {
        if (Process.GetProcessesByName("obs64").Length > 0)
        {
            return;
        }

        string path = settings.OBS.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "obs-studio",
                "bin",
                "64bit",
                "obs64.exe"
            );
        }

        if (!File.Exists(path))
        {
            logger.LogWarning("OBS is not running and {Path} was not found.", path);
            return;
        }

        logger.LogWarning("OBS is not running; starting {Path}.", path);
        Process.Start(
            new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path),
                UseShellExecute = true,
            }
        );
        Thread.Sleep(TimeSpan.FromSeconds(5));
    }

    private void ConnectAndWait()
    {
        EnsureObsProcess();
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

    private bool ShouldRecord() =>
        SessionMedia.ShouldRecord(settings.OBS, context.Current?.LoadedReplay);
}
