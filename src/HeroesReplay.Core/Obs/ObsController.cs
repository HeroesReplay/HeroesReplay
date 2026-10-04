using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.Obs.Recording;
using HeroesReplay.Core.Replays.Context;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using Polly;

namespace HeroesReplay.Core.Obs;

public class ObsController : IObsController
{
    private readonly ILogger<ObsController> logger;
    private readonly IReplayContext context;
    private readonly AppSettings settings;
    private readonly OBSWebsocket obs;
    private readonly CancellationTokenProvider tokenProvider;
    private readonly ObsCoordinator coordinator;
    private bool replayInfoHidden;

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
        var socket = new ObsWebsocketRecordSocket(this.obs);
        var arm = new ObsStreamArm();
        coordinator = new ObsCoordinator(
            logger,
            settings,
            socket,
            new WindowsObsProcess(),
            new RecordingSession(logger, socket, ObsRecordingBudget.Default),
            ObsBackoff.Default,
            Thread.Sleep,
            TimeSpan.FromSeconds(10),
            () => PatchInstalledCollection(),
            arm.IsArmed,
            () =>
                ObsValidator.Validate(
                    new ObsBorrowedReadSession(this.obs),
                    new ObsInspectionSettings(
                        settings.OBS,
                        AppContext.BaseDirectory,
                        settings.Location?.DataDirectory,
                        arm.IsArmed(),
                        arm.FilePath
                    )
                )
        );
    }

    public void BeginSession()
    {
        ObsCollectionApplyResult patch = PatchInstalledCollection();
        coordinator.EnsureIdentified();
        SwapLiveCollection(patch);
    }

    private ObsCollectionApplyResult PatchInstalledCollection()
    {
        try
        {
            bool obsRunning = Process.GetProcessesByName("obs64").Length > 0;
            ObsCollectionApplyResult result = ObsCollectionPatcher.ApplyForInstall(
                AppContext.BaseDirectory,
                settings.Location?.DataDirectory,
                obsRunning,
                ObsNames.SceneCollection(settings.OBS),
                ObsManagedFiles.ForThisUser()
            );
            if (result.Drift)
            {
                logger.LogWarning("OBS collection was not updated. {Reason}", result.Message);
            }
            else if (result.Wrote || result.Deferred && result.Replacement == null)
            {
                logger.LogInformation("{Reason}", result.Message);
            }

            return result;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "OBS collection paths were not updated.");
            return null;
        }
    }

    /// <summary>
    /// Between replays: a newer collection template that waits only because OBS runs goes in
    /// now, through the spare collection, so a release with OBS changes needs no stream stop.
    /// </summary>
    private void SwapLiveCollection(ObsCollectionApplyResult patch)
    {
        RecoverFromSpareCollection();
        if (patch?.Replacement == null)
        {
            return;
        }

        if (settings.OBS?.LiveCollectionSwap != true)
        {
            logger.LogInformation("{Reason}", patch.Message);
            return;
        }

        try
        {
            ObsLiveSwapResult swap = ObsLiveCollectionSwap.Run(
                new ObsWebsocketCollectionSwitch(obs),
                patch.Replacement,
                ObsNames.SceneCollection(settings.OBS),
                ObsManagedFiles.ForThisUser(),
                DateTime.UtcNow
            );
            if (swap.Stranded)
            {
                logger.LogError("OBS collection swap. {Reason}", swap.Message);
            }
            else if (swap.Swapped)
            {
                logger.LogInformation("{Reason}", swap.Message);
            }
            else
            {
                logger.LogWarning("OBS collection was not swapped. {Reason}", swap.Message);
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "OBS collection was not swapped while OBS runs. It is replaced the next time HeroesReplay finds OBS closed."
            );
        }
    }

    /// <summary>
    /// An earlier swap that could not switch back left OBS on the spare collection. The template
    /// record may already match, so this runs before every replay, not only on a new template (#214).
    /// </summary>
    private void RecoverFromSpareCollection()
    {
        try
        {
            ObsLiveSwapResult recovered = ObsLiveCollectionSwap.Recover(
                new ObsWebsocketCollectionSwitch(obs),
                ObsNames.SceneCollection(settings.OBS)
            );
            if (recovered?.Stranded == true)
            {
                logger.LogError("OBS collection swap. {Reason}", recovered.Message);
            }
            else if (recovered != null)
            {
                logger.LogWarning("OBS collection swap. {Reason}", recovered.Message);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not check whether OBS was left on the spare collection.");
        }
    }

    public void EndSession()
    {
        coordinator.Disconnect();
    }

    public void ConfigureFromContext()
    {
        try
        {
            EnsureConnected();
            replayInfoHidden = false;
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

    public ObsRecordingResult StartRecording()
    {
        string reason = SessionMedia.HasRequestor(context.Current?.LoadedReplay)
            ? "viewer request"
            : "every replay";
        return coordinator.StartRecording(
            ShouldRecord,
            CurrentReplayId,
            reason,
            () => SetRecordDirectoryForCurrentReplay()
        );
    }

    private void SetRecordDirectoryForCurrentReplay()
    {
        string directory = context.Current?.Directory?.FullName;
        if (!ShouldRecord() || string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            obs.SetRecordDirectory(directory);
            logger.LogInformation(
                "OBS record directory for replay {ReplayId} is {Directory}.",
                CurrentReplayId,
                directory
            );
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Could not set the OBS record directory for replay {ReplayId}.",
                CurrentReplayId
            );
        }
    }

    public ObsRecordingResult StopRecording() => coordinator.StopRecording(CurrentReplayId);

    public ObsStreamResult StartStreaming() => coordinator.ReconcileStream().Stream;

    public ObsStreamResult StopStreaming() => coordinator.Shutdown().Stream;

    public ObsRuntimeSnapshot ReadObsState() => coordinator.State;

    private int? CurrentReplayId => context.Current?.LoadedReplay?.ReplayId;

    public bool IsStreaming() => coordinator.IsStreaming();

    public void UpdateReplayInfoVisibility(TimeSpan matchTime)
    {
        if (replayInfoHidden || !settings.OBS.Enabled)
        {
            return;
        }

        if (ReplayInfoVisibility.ShouldShow(matchTime, settings.OBS.InfoVisibleFor))
        {
            return;
        }

        replayInfoHidden = true;
        try
        {
            EnsureConnected();
            SetSceneItemVisible(
                settings.OBS.GameSceneName,
                settings.OBS.InfoSourceName,
                visible: false
            );
            logger.LogInformation(
                "OBS {Source} hidden at {MatchTime}. It is shown for the first {Window} of the match.",
                settings.OBS.InfoSourceName,
                matchTime,
                settings.OBS.InfoVisibleFor > TimeSpan.Zero
                    ? settings.OBS.InfoVisibleFor
                    : ReplayInfoVisibility.Default
            );
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not hide {Source}.", settings.OBS.InfoSourceName);
        }
    }

    public void SwapToGameScene()
    {
        SceneRetry()
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
        SceneRetry()
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

    public async Task CycleReportAsync(CancellationToken cancellationToken = default)
    {
        if (!context.Current.LoadedReplay.ReplayId.HasValue)
        {
            logger.LogInformation(
                "Skipping Heroes Profile report scenes because the replay has no Heroes Profile id."
            );
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            tokenProvider.Token,
            cancellationToken
        );
        try
        {
            await SceneRetry()
                .ExecuteAsync(t => new ValueTask<bool>(CycleOnceAsync(t)), linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "Report scenes stopped early: the next match is showing or spectate is stopping."
            );
        }
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

    private async Task<bool> TryCycleSceneAsync(
        ReportScene source,
        CancellationToken cancellationToken
    )
    {
        try
        {
            obs.SetCurrentProgramScene(source.SceneName);
            logger.LogInformation($"set scene to: {source.SceneName}");
            if (source.DisplayTime > TimeSpan.Zero)
            {
                await Task.Delay(source.DisplayTime, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Report scene {Scene} stopped early: the next match is showing or spectate is stopping.",
                source.SceneName
            );
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
                if (IsMatchReport(segment))
                {
                    browserSettings["css"] = MatchReportBrowserCss.WithScroll(
                        browserSettings["css"]?.ToString(),
                        segment.DisplayTime
                    );
                    browserSettings["height"] = MatchReportBrowserCss.SourceHeight;
                }

                obs.SetInputSettings(source.InputName, browserSettings);
                if (IsMatchReport(segment))
                {
                    StopObsScroll(segment);
                }

                return true;
            }
            catch (Exception e)
            {
                logger.LogError(e, $"could not set {segment.SceneName} URL to: {url}");
            }
        }

        return false;
    }

    private static bool IsMatchReport(ReportScene segment) =>
        string.Equals(segment.SceneName, "match-report", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The page scrolls itself now. A collection from an earlier build still has the OBS
    /// Scroll filter on this source, and it would move the page a second time.
    /// </summary>
    private void StopObsScroll(ReportScene segment)
    {
        try
        {
            obs.SetSourceFilterEnabled(segment.SourceName, "Scroll", false);
            logger.LogInformation(
                "Match report scrolls itself for {Duration}. The OBS Scroll filter is off.",
                segment.DisplayTime
            );
        }
        catch (Exception e)
        {
            // A collection from this build has no Scroll filter on the source.
            logger.LogDebug(e, "No OBS Scroll filter to turn off on {Source}.", segment.SourceName);
        }
    }

    private void ApplyReportBrowserCss(JObject browserSettings)
    {
        browserSettings["css"] = MatchReportBrowserCss.Build(
            settings.OBS.ReportBrowserCss,
            settings.OBS.HideReportHeader
        );
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

    private void EnsureConnected() => coordinator.EnsureIdentified();

    private ResiliencePipeline<bool> SceneRetry() =>
        ResilienceRetry.Constant<bool>(
            retries: 5,
            delay: TimeSpan.FromSeconds(5),
            retry: outcome => ResilienceRetry.Failed(outcome, succeeded => succeeded == false),
            onRetry: args =>
            {
                if (args.Outcome.Exception != null)
                {
                    logger.LogWarning(args.Outcome.Exception, "Could not control OBS");
                }
                else
                {
                    logger.LogWarning("Could not control OBS");
                }
            }
        );

    private async Task<bool> CycleOnceAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

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
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Remaining report scenes are skipped: the next match is showing or spectate is stopping."
                );
                return true;
            }

            await TryCycleSceneAsync(source, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private bool ShouldRecord() =>
        SessionMedia.ShouldRecord(settings.OBS, context.Current?.LoadedReplay);
}
