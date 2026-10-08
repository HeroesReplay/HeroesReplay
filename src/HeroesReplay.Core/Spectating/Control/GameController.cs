using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using HeroesClientSDK;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Replays.Context;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating.Capture;
using HeroesReplay.Core.Spectating.Screens;
using HeroesReplay.Core.Telemetry;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using static PInvoke.User32;

namespace HeroesReplay.Core.Spectating.Control;

public class GameController : IGameController
{
    private readonly OcrEngine ocrEngine;
    private readonly CancellationTokenProvider tokenProvider;
    private readonly ILogger<GameController> logger;
    private readonly IReplayContext context;
    private readonly AppSettings settings;
    private readonly IObsController obsController;
    private readonly IGameCapture capture;
    private readonly IReplayOpener replayOpener;
    private readonly StormClientConfigurator clientConfigurator;

    private readonly object controllerLock = new object();

    // One attached client per process for every memory reader, the timer's clock included (#382).
    private readonly SharedClientProcess clientProcess;
    private readonly MatchClock matchClock = new();
    private readonly LoadingScreen loadingScreen = new();
    private readonly ClientScreen clientScreens = new();
    private readonly IClientWindows clientWindows = new Win32ClientWindows();
    private readonly ScreenShadow screenShadow;
    private LoadingScreenSample lastScreen;
    private ClientScreenSample lastClientScreen;
    private string lastClockReason = "no-process";
    private Process cachedProcess;
    private bool replayFileOpened;
    private string openedReplayPath;

    // The replay this spectator last handed the running client, so a match on screen can be
    // told apart from another replay's. Null when unknown (no client, or a spectate restart).
    private string replayOnClient;
    private ReplayClientPatch launchPatch = ReplayClientPatch.Current;

    // A replay on an older build that is not installed: where its exe will appear, and when the
    // replay was handed to HeroesSwitcher so Blizzard downloads it. Null when not downloading.
    private string downloadExePath;
    private DateTimeOffset? downloadOpenedAt;
    private int? launcherRecoveryReplayId;
    private int launcherRecoveryAttempt;

    public bool ReplayFileOpened => replayFileOpened;

    private TimeSpan LaunchWaitLimit =>
        settings.Spectate?.LaunchWaitLimit > TimeSpan.Zero
            ? settings.Spectate.LaunchWaitLimit
            : ReplayClientRoute.DefaultLaunchWaitLimit;

    private TimeSpan BuildDownloadLimit =>
        ClientDownloadHold.DownloadLimit(settings.Spectate?.BuildDownloadLimit ?? TimeSpan.Zero);

    private TimeSpan BuildDownloadHold =>
        ClientDownloadHold.Hold(settings.Spectate?.BuildDownloadHold ?? TimeSpan.Zero);

    public static readonly VirtualKey[] Keys =
    {
        VirtualKey.VK_KEY_1,
        VirtualKey.VK_KEY_2,
        VirtualKey.VK_KEY_3,
        VirtualKey.VK_KEY_4,
        VirtualKey.VK_KEY_5,
        VirtualKey.VK_KEY_6,
        VirtualKey.VK_KEY_7,
        VirtualKey.VK_KEY_8,
        VirtualKey.VK_KEY_9,
        VirtualKey.VK_KEY_0,
    };

    public GameController(
        ILogger<GameController> logger,
        IReplayContext context,
        AppSettings settings,
        IObsController obsController,
        IGameCapture capture,
        IReplayOpener replayOpener,
        StormClientConfigurator clientConfigurator,
        OcrEngine engine,
        CancellationTokenProvider tokenProvider,
        SharedClientProcess clientProcess
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.clientProcess =
            clientProcess ?? throw new ArgumentNullException(nameof(clientProcess));
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.obsController =
            obsController ?? throw new ArgumentNullException(nameof(obsController));
        this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
        this.replayOpener = replayOpener ?? throw new ArgumentNullException(nameof(replayOpener));
        this.clientConfigurator =
            clientConfigurator ?? throw new ArgumentNullException(nameof(clientConfigurator));
        this.ocrEngine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.tokenProvider =
            tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        screenShadow = new ScreenShadow(
            logger,
            TimeProvider.System,
            settings.OCR?.ShadowFrameInterval
        );
    }

    public async Task<ClientHoldReason> LaunchAsync()
    {
        using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.launch");
        var replay = context.Current.LoadedReplay.Replay;
        HeroesReplayTelemetry.TagReplay(
            activity,
            context.Current.LoadedReplay.FileInfo?.FullName,
            replay?.Map,
            context.Current.LoadedReplay.ReplayId,
            replay?.ReplayVersion
        );

        replayFileOpened = false;
        try
        {
            return await LaunchAndWait().ConfigureAwait(false);
        }
        finally
        {
            // A download clock belongs to one launch (with the report's preload before it).
            // The same replay retried later starts a new one.
            downloadOpenedAt = null;
        }
    }

    private async Task<ClientHoldReason> LaunchAndWait()
    {
        using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.launch.replay");
        activity?.SetTag("replay.path", context.Current.LoadedReplay.FileInfo?.FullName);
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            activity?.SetTag("launch.replay_attempt", attempt);
            ColdBoot boot = await StartReplayAndWaitAsync().ConfigureAwait(false);
            if (boot.Hold != ClientHoldReason.None)
            {
                return boot.Hold;
            }

            if (!boot.RetryDisconnect || attempt == 2)
            {
                return ClientHoldReason.None;
            }

            logger.LogWarning(
                "Battle.net disconnected while loading the replay. Closing the client and trying once more."
            );
            Kill();
            replayFileOpened = false;
            await Task.Delay(ClientRelaunch.SettleAfterExit, tokenProvider.Token)
                .ConfigureAwait(false);
        }

        return ClientHoldReason.None;
    }

    private LauncherRecoveryAction NextLauncherRecovery(ClientHoldReason hold)
    {
        if (hold != ClientHoldReason.VersionMismatch)
        {
            return LauncherRecoveryAction.None;
        }

        int? replayId = context.Current?.LoadedReplay?.ReplayId;
        if (launcherRecoveryReplayId != replayId)
        {
            launcherRecoveryReplayId = replayId;
            launcherRecoveryAttempt = 0;
        }

        LauncherRecoveryAction action = LauncherRecoveryPlan.Decide(hold, launcherRecoveryAttempt);
        launcherRecoveryAttempt++;
        return action;
    }

    public async Task<bool> StartAuthenticatedReplayAsync(
        string replayPath,
        string replayVersion = null
    )
    {
        ReplayBoot boot = await BeginReplayAsync(replayPath, replayVersion).ConfigureAwait(false);
        if (replayFileOpened || boot.Auth == ReplayLaunchAuth.AlreadyInMatch)
        {
            openedReplayPath = replayPath;
        }

        return boot.OpenedFromHome || boot.Auth == ReplayLaunchAuth.AlreadyInMatch;
    }

    public Task<bool> OpenReplayFromHomeScreenAsync(string replayPath) =>
        Task.FromResult(OpenReplayFromHomeScreen(replayPath));

    private bool OpenReplayFromHomeScreen(string replayPath)
    {
        // While Blizzard fetches the replay's build, the menu on screen is the newest exe's
        // handoff. Opening the file again there would start the switch over.
        if (AwaitingBuildDownload())
        {
            return false;
        }

        if (!IsLaunched() || !IsHomeScreen())
        {
            return false;
        }

        OpenReplayFromHome(replayPath);
        return true;
    }

    private bool AwaitingBuildDownload() => BuildDownloadNow() == BuildDownloadState.Waiting;

    private BuildDownloadState BuildDownloadNow()
    {
        bool exeExists =
            !string.IsNullOrWhiteSpace(downloadExePath) && File.Exists(downloadExePath);
        TimeSpan waited = downloadOpenedAt is DateTimeOffset openedAt
            ? DateTimeOffset.UtcNow - openedAt
            : TimeSpan.Zero;
        return ClientDownloadHold.Check(
            launchPatch,
            downloadOpenedAt != null,
            exeExists,
            waited,
            BuildDownloadLimit
        );
    }

    /// <summary>The replay went to HeroesSwitcher. On a missing build, the download clock starts.</summary>
    private void NoteSwitcherOpen()
    {
        if (launchPatch == ReplayClientPatch.Download)
        {
            downloadOpenedAt ??= DateTimeOffset.UtcNow;
        }
    }

    private bool SameReplayAlreadyOpening(string replayPath)
    {
        return !string.IsNullOrWhiteSpace(openedReplayPath)
            && !string.IsNullOrWhiteSpace(replayPath)
            && string.Equals(openedReplayPath, replayPath, StringComparison.OrdinalIgnoreCase);
    }

    private void OpenReplayFromHome(string replayPath)
    {
        replayFileOpened = true;
        logger.LogInformation("Client is on the home screen. Opening the replay.");
        CloseIdleSwitcher();
        replayOpener.Open(replayPath);
        replayOnClient = replayPath;
    }

    private readonly record struct ReplayBoot(ReplayLaunchAuth Auth, bool OpenedFromHome);

    private async Task<ReplayBoot> BeginReplayAsync(string replayPath, string replayVersion)
    {
        string gameDirectory = settings.Location?.GameInstallDirectory;
        string archiveDirectory = ClientBuildArchive.ResolveDirectory(
            settings.Location?.RetainedClientDirectory,
            settings.Location?.DataDirectory
        );
        foreach (
            ClientBuildKeepItem kept in ClientBuildArchive.Preserve(
                InstalledClientCatalog.Clients(gameDirectory),
                archiveDirectory,
                settings.Spectate?.MinimumGameVersion
            )
        )
        {
            if (kept.Result == ClientBuildKeep.Copied)
            {
                logger.LogInformation(
                    "Kept Heroes build {Version} so a later Battle.net reclaim can still launch it.",
                    kept.Version
                );
            }
            else if (kept.Result == ClientBuildKeep.Skipped)
            {
                logger.LogWarning("Could not keep Heroes build {Version}.", kept.Version);
            }
        }

        if (ClientBuildArchive.Restore(gameDirectory, archiveDirectory, replayVersion))
        {
            logger.LogInformation(
                "Restored Heroes build {Version} into Versions before opening the replay.",
                replayVersion
            );
        }

        IReadOnlyList<string> installed = InstalledClientCatalog.FileVersions(gameDirectory);
        IReadOnlyList<string> heldBuilds = ClientDownloadHold.ActiveIn(
            settings.Location?.DataDirectory,
            DateTimeOffset.UtcNow,
            BuildDownloadHold
        );
        launchPatch = ReplayClientRoute.Classify(replayVersion, installed, heldBuilds);
        if (launchPatch == ReplayClientPatch.Download)
        {
            // The report may already have handed this replay to HeroesSwitcher. That download
            // keeps its start time; any other replay starts a new one.
            if (!SameReplayAlreadyOpening(replayPath))
            {
                downloadOpenedAt = null;
            }

            downloadExePath = InstalledClientCatalog.ExePathFor(gameDirectory, replayVersion);
        }
        else
        {
            downloadOpenedAt = null;
            downloadExePath = null;
        }
        if (installed.Count == 0)
        {
            logger.LogWarning(
                "No Heroes clients were found under Versions. This replay uses the current-patch sign-in."
            );
        }

        RunningClientBuild running = ReadRunningBuild(replayVersion);
        bool presented = false;
        bool home = false;
        bool otherReplay = false;
        if (running == RunningClientBuild.None)
        {
            replayOnClient = null;
        }
        else if (running == RunningClientBuild.Matches)
        {
            presented = await IsReplayPresentedAsync(context.Current?.LoadedReplay)
                .ConfigureAwait(false);
            otherReplay =
                presented && ReplayClientRoute.OtherReplayOnClient(replayOnClient, replayPath);
            home = !presented && IsHomeScreen();
        }

        ReplayLaunchAuth auth = ReplayClientRoute.Decide(
            launchPatch,
            running,
            home,
            presented,
            otherReplay
        );
        logger.LogInformation(
            "Replay {Version} is the {Patch} patch. Running client is {Running}. Launch step is {Auth}. Installed: {Installed}.",
            string.IsNullOrWhiteSpace(replayVersion) ? "(unknown)" : replayVersion,
            launchPatch,
            running,
            auth,
            installed.Count == 0 ? "(none)" : string.Join(", ", installed)
        );

        if (auth == ReplayLaunchAuth.RelaunchCurrent)
        {
            logger.LogWarning(
                "The current-patch client is playing {Other}, not this replay. Closing it and asking the logged-in Battle.net to start Heroes again.",
                Path.GetFileName(replayOnClient)
            );
            Kill();
            replayFileOpened = false;
            await Task.Delay(ClientRelaunch.SettleAfterExit, tokenProvider.Token)
                .ConfigureAwait(false);
            if (IsGameProcessRunning())
            {
                logger.LogWarning(
                    "The other replay's client is still running. The replay file was not opened."
                );
                return new ReplayBoot(ReplayLaunchAuth.Wait, false);
            }
        }

        if (auth == ReplayLaunchAuth.Unavailable)
        {
            if (ReplayClientRoute.Classify(replayVersion, installed) == ReplayClientPatch.Download)
            {
                logger.LogWarning(
                    "Replay {Version} needs a Heroes client that is not installed, and Blizzard did not download that build within the last {Hold}. Nothing was launched. The replay stays queued.",
                    replayVersion,
                    BuildDownloadHold
                );
            }
            else
            {
                logger.LogWarning(
                    "Replay {Version} needs a Heroes client newer than every installed build. Battle.net has not updated Heroes. The current patch was not launched.",
                    replayVersion
                );
            }

            return new ReplayBoot(auth, false);
        }

        if (auth == ReplayLaunchAuth.AlreadyInMatch)
        {
            // The report preloaded this replay, or spectate restarted under it. A replay that
            // is already playing is a normal start: the session tracks its clock from here.
            replayFileOpened = true;
            replayOnClient = replayPath;
            ShowGameScene("match already on screen");
            return new ReplayBoot(auth, true);
        }

        if (auth == ReplayLaunchAuth.OpenMatchingBuild)
        {
            replayFileOpened = true;
            if (
                !ReplayClientRoute.OpensTheMatchingBuildAgain(
                    auth,
                    SameReplayAlreadyOpening(replayPath)
                )
            )
            {
                logger.LogInformation(
                    "Matching Heroes client is already opening this replay. The file is not opened again. The client was not closed."
                );
                return new ReplayBoot(ReplayLaunchAuth.Wait, false);
            }

            logger.LogInformation(
                "Matching Heroes client is already running. Opening the replay through HeroesSwitcher. The client was not closed."
            );
            replayOpener.Open(replayPath);
            NoteSwitcherOpen();
            replayOnClient = replayPath;
            return new ReplayBoot(ReplayLaunchAuth.Wait, false);
        }

        if (auth == ReplayLaunchAuth.Wait)
        {
            logger.LogInformation(
                running == RunningClientBuild.Unreadable
                    ? "The running Heroes version could not be read. The replay file stays closed. The launch re-checks the client and recovers it after {Limit}."
                    : "Matching current-patch client is up without the home screen, a match, or the match clock. The replay file stays closed until the signed-in menu is visible. The launch re-checks the client and recovers it after {Limit}.",
                LaunchWaitLimit
            );
            return new ReplayBoot(auth, false);
        }

        if (auth == ReplayLaunchAuth.OpenFromHome)
        {
            OpenReplayFromHome(replayPath);
            return new ReplayBoot(auth, true);
        }

        if (running == RunningClientBuild.Differs && auth != ReplayLaunchAuth.OpenInstalledBuild)
        {
            logger.LogInformation(
                "Closing the running Heroes client because it is not replay build {Version}.",
                replayVersion
            );
            Kill();
            replayFileOpened = false;
            await Task.Delay(ClientRelaunch.SettleAfterExit, tokenProvider.Token)
                .ConfigureAwait(false);
            if (IsGameProcessRunning())
            {
                logger.LogWarning(
                    "The other Heroes build is still running. The replay file was not opened."
                );
                return new ReplayBoot(ReplayLaunchAuth.Wait, false);
            }
        }

        if (auth == ReplayLaunchAuth.OpenInstalledBuild)
        {
            if (
                launchPatch == ReplayClientPatch.Download
                && downloadOpenedAt != null
                && SameReplayAlreadyOpening(replayPath)
                && IsGameProcessRunning()
            )
            {
                // The report handed this replay to HeroesSwitcher and Blizzard is still fetching
                // the build. A second open would restart the switch.
                logger.LogInformation(
                    "HeroesSwitcher already has replay {Version} and Blizzard is still downloading that build. The file is not opened again. The client was not closed.",
                    replayVersion
                );
                replayFileOpened = true;
                replayOnClient = replayPath;
                return new ReplayBoot(auth, false);
            }

            logger.LogInformation(
                launchPatch == ReplayClientPatch.Download
                    ? "Replay {Version} needs a Heroes build that is not installed. Opening it through HeroesSwitcher so Blizzard downloads that client. The running client was not closed. Battle.net Play was not used."
                    : "Opening previous-patch replay {Version} through HeroesSwitcher. The running client was not closed. Battle.net Play was not used.",
                replayVersion
            );
            CloseIdleSwitcher();
            replayFileOpened = true;
            replayOpener.Open(replayPath);
            replayOnClient = replayPath;
            NoteSwitcherOpen();
            return new ReplayBoot(auth, false);
        }

        await WaitForAuthenticatedClientAsync().ConfigureAwait(false);
        if (IsLaunched() && IsHomeScreen())
        {
            OpenReplayFromHome(replayPath);
            return new ReplayBoot(auth, true);
        }

        if (IsLaunched())
        {
            logger.LogInformation(
                "Heroes is open without the home screen. The replay file stays closed until the signed-in menu is visible."
            );
        }

        return new ReplayBoot(auth, false);
    }

    private RunningClientBuild ReadRunningBuild(string replayVersion)
    {
        if (!IsLaunched())
        {
            return RunningClientBuild.None;
        }

        if (string.IsNullOrWhiteSpace(replayVersion))
        {
            return RunningClientBuild.Unreadable;
        }

        // The client the memory readers share: read-only, its main module's file version (#382).
        (bool ok, string reason, HeroesClientVersion detected) = clientProcess.Read(
            cachedProcess,
            running =>
                (running?.Ok == true, running?.Reason ?? "no-process", running?.DetectedVersion)
        );
        if (!ok)
        {
            logger.LogWarning("Could not read the running Heroes file version ({Reason}).", reason);
        }

        return ReplayClientRoute.RunningBuild(detected, replayVersion);
    }

    private async Task WaitForAuthenticatedClientAsync()
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        int requests = 0;
        DateTimeOffset lastRequest = DateTimeOffset.MinValue;
        while (!IsLaunched() && DateTimeOffset.UtcNow < deadline)
        {
            TimeSpan sinceLast =
                requests == 0 ? TimeSpan.Zero : DateTimeOffset.UtcNow - lastRequest;
            if (ClientRelaunch.ShouldRequestLaunch(IsLaunched(), requests, sinceLast))
            {
                logger.LogInformation("Asking the logged-in Battle.net to start Heroes.");
                replayOpener.RequestAuthenticatedClient();
                requests++;
                lastRequest = DateTimeOffset.UtcNow;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), tokenProvider.Token)
                .ConfigureAwait(false);
        }
    }

    private readonly record struct ColdBoot(bool RetryDisconnect, ClientHoldReason Hold);

    private async Task<ColdBoot> StartReplayAndWaitAsync()
    {
        string replayPath = context.Current.LoadedReplay.FileInfo.FullName;
        string replayVersion = context.Current.LoadedReplay.Replay?.ReplayVersion;
        ReplayBoot boot = await BeginReplayAsync(replayPath, replayVersion).ConfigureAwait(false);
        if (boot.Auth == ReplayLaunchAuth.Unavailable)
        {
            return new ColdBoot(RetryDisconnect: false, ClientHoldReason.BuildNotInstalled);
        }

        if (boot.Auth == ReplayLaunchAuth.AlreadyInMatch)
        {
            return new ColdBoot(RetryDisconnect: false, ClientHoldReason.None);
        }

        bool openedFromHome = boot.OpenedFromHome;
        bool recoveredLogin = false;
        bool loggedMismatch = false;
        bool loggedPreparing = false;
        bool sawGameDataStartup = false;
        bool sawGameDataDownload = false;
        bool loggedDownload = false;
        bool loggedHandoff = false;
        int dataRestarts = 0;
        bool interfaceRestarted = false;
        int blankRelaunches = 0;
        bool blankTiming = false;
        DateTimeOffset blankSince = default;
        DateTimeOffset started = DateTimeOffset.UtcNow;
        DateTimeOffset deadline = started.Add(ClientRelaunch.ColdBootLimit);
        bool openedThroughSwitcher =
            boot.Auth == ReplayLaunchAuth.OpenInstalledBuild
            || (boot.Auth == ReplayLaunchAuth.Wait && replayFileOpened);
        bool clientAlreadyRunning =
            boot.Auth == ReplayLaunchAuth.Wait || boot.Auth == ReplayLaunchAuth.OpenInstalledBuild;
        bool openedOnMatchingExe = ReplayClientRoute.TreatsAsMatchingOpen(
            boot.Auth,
            replayFileOpened
        );
        bool checkedLaunchFile = false;
        int defaultHudCorrections = 0;
        string expectedInterface =
            settings.Client?.ReplayInterface ?? ClientSettings.AhliObsInterfaceFile;

        // A Wait step is bounded (#249): it re-checks the client every pass and recovers it once
        // when nothing usable has been on screen for Spectate:LaunchWaitLimit.
        bool boundedWait = boot.Auth == ReplayLaunchAuth.Wait;
        DateTimeOffset stuckSince = started;
        int waitRecoveries = 0;

        // The game-data DOWNLOADING dialog, from memory only (#292): a shown CProgressBarDialog.
        async Task<bool> HoldForGameDataDownloadAsync(bool downloading)
        {
            if (!downloading)
            {
                return false;
            }

            // A download is the client at work, not a stuck wait, and not a stalled launch.
            stuckSince = DateTimeOffset.UtcNow;
            ServiceHeartbeat.RecordLaunching();

            if (
                !ClientInterfacePlan.DownloadBelongsToReplayClient(
                    true,
                    ReadRunningBuild(replayVersion) == RunningClientBuild.Matches
                )
            )
            {
                await Task.Delay(settings.OCR.CheckSleepDuration, tokenProvider.Token)
                    .ConfigureAwait(false);
                return true;
            }

            sawGameDataDownload = true;
            DateTimeOffset extended = ClientInterfacePlan.ExtendForGameDataDownload(
                started,
                deadline,
                DateTimeOffset.UtcNow
            );
            if (extended > deadline)
            {
                deadline = extended;
            }

            if (!loggedDownload)
            {
                loggedDownload = true;
                logger.LogInformation(
                    "Heroes is downloading game data for this client. The client stays open. Battle.net was not clicked."
                );
            }

            await Task.Delay(settings.OCR.CheckSleepDuration, tokenProvider.Token)
                .ConfigureAwait(false);
            return true;
        }

        // A replay on an older build that is not installed: Blizzard downloads that client while
        // the newest exe the switcher started hands off. Until the build's exe is in Versions,
        // the launch waits for it (bounded by Spectate:BuildDownloadLimit), and nothing on
        // screen is closed or reopened. After that the previous-patch rules below take over.
        bool loggedBuildWait = false;
        bool loggedBuildArrived = false;

        ColdBoot FailBuildDownload(string reason)
        {
            DateTimeOffset failedAt = DateTimeOffset.UtcNow;
            logger.LogWarning(
                "Blizzard did not download Heroes build {Version} ({Reason}). Closing Heroes so the next replay can start. The build counts as not installed until {Until:o} and the replay stays queued. Battle.net was not clicked. Update was not clicked.",
                replayVersion,
                reason,
                failedAt.Add(BuildDownloadHold)
            );
            Kill();
            replayFileOpened = false;
            downloadOpenedAt = null;
            CloseIdleSwitcher();
            try
            {
                ClientDownloadHold.Record(
                    ClientDownloadHold.FilePath(settings.Location?.DataDirectory),
                    replayVersion,
                    failedAt,
                    BuildDownloadHold
                );
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(
                    e,
                    "Could not record the failed download of Heroes build {Version}.",
                    replayVersion
                );
            }

            return new ColdBoot(RetryDisconnect: false, ClientHoldReason.BuildNotInstalled);
        }

        while (DateTimeOffset.UtcNow < deadline)
        {
            BuildDownloadState buildDownload = BuildDownloadNow();
            if (buildDownload == BuildDownloadState.Failed)
            {
                return FailBuildDownload(
                    $"its exe did not appear in Versions within {BuildDownloadLimit}"
                );
            }

            if (buildDownload == BuildDownloadState.Waiting)
            {
                // The download is the client at work: not a stuck wait, not a stalled launch,
                // and the cold-boot deadline starts when the build's exe arrives.
                ServiceHeartbeat.RecordLaunching();
                stuckSince = DateTimeOffset.UtcNow;
                DateTimeOffset afterArrival = ClientRelaunch.DeadlineAfterInterfaceRestart(
                    DateTimeOffset.UtcNow
                );
                if (afterArrival > deadline)
                {
                    deadline = afterArrival;
                }

                if (!loggedBuildWait)
                {
                    loggedBuildWait = true;
                    logger.LogInformation(
                        "Waiting for Blizzard to download Heroes build {Version} into {Exe} (limit {Limit}). The newest exe's handoff stays up. Battle.net was not clicked.",
                        replayVersion,
                        downloadExePath ?? "(unknown folder)",
                        BuildDownloadLimit
                    );
                }
            }
            else if (buildDownload == BuildDownloadState.Arrived && !loggedBuildArrived)
            {
                loggedBuildArrived = true;
                logger.LogInformation(
                    "Heroes build {Version} arrived in Versions {Waited} after the replay went to HeroesSwitcher. The previous-patch launch continues.",
                    replayVersion,
                    downloadOpenedAt is DateTimeOffset openedAt
                        ? DateTimeOffset.UtcNow - openedAt
                        : TimeSpan.Zero
                );
            }

            if (
                ClientRelaunch.MatchingOpenLeftNoProcess(
                    openedOnMatchingExe,
                    IsGameProcessRunning(),
                    DateTimeOffset.UtcNow - started
                )
            )
            {
                logger.LogInformation(
                    "The matching client did not stay open. The next replay starts. Battle.net was not clicked."
                );
                return new ColdBoot(RetryDisconnect: false, ClientHoldReason.ClientNotReady);
            }

            WindowRead window = await ReadWindowAsync().ConfigureAwait(false);
            string text = window.Text;
            ClientScreenSample? clientScreen = ReadClientScreen();

            // The email and password form also says "Battle.net" and "Log in". Memory's login
            // read, or the login-form words, keep it off the disconnect path (#385).
            if (BattleNetDisconnect.IsShown(text, clientScreen?.OnLogin))
            {
                logger.LogWarning("Battle.net disconnect dialog: {Text}", text);
                return new ColdBoot(RetryDisconnect: true, ClientHoldReason.None);
            }

            ShadowScreen(
                ScreenState.VersionMismatch,
                ClientScreenText.IsVersionMismatch(text),
                text
            );
            ShadowScreen(
                ScreenState.RegionUnavailable,
                ClientScreenText.IsRegionUnavailable(text),
                text
            );

            // Any game-launch failure the client shows (its launch result in a CStandardDialog,
            // read from memory) is an invalid client, handled like the version dialog (#292).
            LaunchFailure? launchFailure = ClientLaunchFailure.Read(clientScreen);
            ClientHoldReason hold =
                launchFailure != null
                    ? ClientLaunchFailure.Classify(clientScreen)
                    : ClientHold.Classify(text);
            if (launchFailure is LaunchFailure failure && !loggedMismatch)
            {
                logger.LogWarning(
                    "Heroes shows game-launch result {Code} {Key} in a {Dialog} (client memory). The client cannot play this replay.",
                    failure.Code,
                    failure.Key,
                    ClientLaunchFailure.ResultDialog
                );
            }

            if (ClientDownloadHold.DialogFailsDownload(buildDownload, hold))
            {
                return FailBuildDownload(
                    launchFailure is LaunchFailure failed
                        ? $"game-launch result {failed}"
                        : "the version mismatch dialog"
                );
            }

            if (hold != ClientHoldReason.None)
            {
                LauncherRecoveryAction recovery = NextLauncherRecovery(hold);
                if (!loggedMismatch)
                {
                    loggedMismatch = true;
                    if (recovery == LauncherRecoveryAction.None)
                    {
                        logger.LogWarning(
                            "Heroes is on the {Hold} dialog. The replay stays queued and this client stays open.",
                            hold
                        );
                    }
                    else
                    {
                        logger.LogWarning(
                            "Heroes is on the version mismatch dialog. Launcher plan {Action}. The replay stays queued. Battle.net was not clicked. Update was not clicked.",
                            recovery
                        );
                    }
                }

                return new ColdBoot(RetryDisconnect: false, hold);
            }

            bool awardScreen = MatchEndBanner.EndsLaunchWait(text);
            ShadowScreen(ScreenState.EndScreen, awardScreen, text);
            if (awardScreen)
            {
                logger.LogInformation("The client is on the award screen. This replay is over.");
                return new ColdBoot(RetryDisconnect: false, ClientHoldReason.AwardScreen);
            }

            bool downloading = clientScreen?.OnDownload == true;
            if (await HoldForGameDataDownloadAsync(downloading).ConfigureAwait(false))
            {
                continue;
            }

            // Memory alone decides the map loading screen (#292): LoadingScreen after a menu, else
            // ClientScreen's map panel, which also reads before any menu. The screen is not OCR'd
            // for it. A match in memory is the replay already playing, with or without a clock.
            LoadingScreenSample? screen = ReadScreenInMemory();
            bool? memoryLoading = ReplayLoadCue.MapLoadingInMemory(screen, ReadClientScreen());
            bool inMatch =
                screen?.InMatch == true
                && !ReplayClientRoute.OtherReplayOnClient(replayOnClient, replayPath);
            bool loading = memoryLoading == true;
            bool timer = await IsMatchClockRunning().ConfigureAwait(false);
            // The email/password form from memory only (#292); the window is not OCR'd for it.
            bool loginForm = LoginFormCue.Sees(clientScreen?.OnLogin);
            // A login form while the build downloads is the newest exe's handoff. Closing it
            // would stop the download.
            if (
                !recoveredLogin
                && buildDownload != BuildDownloadState.Waiting
                && !loading
                && !timer
                && !inMatch
                && loginForm
            )
            {
                recoveredLogin = true;
                ReplaySignInRecovery recovery = ReplayClientRoute.Recover(launchPatch, 0);
                if (recovery == ReplaySignInRecovery.OpenPreviousBuild)
                {
                    logger.LogWarning(
                        "Previous-patch client is on the login form. Opening that build again through HeroesSwitcher."
                    );
                    await ReopenPreviousBuildAsync(replayPath).ConfigureAwait(false);
                }
                else if (recovery == ReplaySignInRecovery.LaunchCurrent)
                {
                    logger.LogWarning(
                        "Heroes is on the login form. Starting the signed-in client again before opening the replay."
                    );
                    await RestartAuthenticatedClientAsync().ConfigureAwait(false);
                }

                openedFromHome = false;
                openedOnMatchingExe = false;
                blankTiming = false;
                stuckSince = DateTimeOffset.UtcNow;
                continue;
            }

            // Home from memory only (#292); the window is not OCR'd for the menu's words.
            bool home =
                !openedFromHome
                && !loading
                && !timer
                && !inMatch
                && IsGameProcessRunning()
                && SeesHome();
            // "Preparing game data" is a native Win32 dialog of the client exe, read from the
            // client's windows (#292): a visible #32770 with a msctls_progress32 child. Not OCR.
            GameDataWindowSample preparing = GameDataProgressWindow.Read(
                clientWindows,
                GetGameProcess()?.Id
            );
            bool startup = preparing.Shown == true;
            RunningClientBuild runningBuild = ReadRunningBuild(replayVersion);
            bool differentBuild = runningBuild == RunningClientBuild.Differs;
            bool matchingBuild = runningBuild == RunningClientBuild.Matches;
            if (
                !checkedLaunchFile
                && !IsGameProcessRunning()
                && openedThroughSwitcher
                && LaunchFileLoadsDefaultHud(launchPatch, expectedInterface)
            )
            {
                try
                {
                    clientConfigurator.Configure();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(e, "Could not write AhliObs while Heroes was stopped.");
                }
            }

            if (matchingBuild && !checkedLaunchFile)
            {
                checkedLaunchFile = true;
                ReplayInterfaceValues launchFile = clientConfigurator.ReadReplayInterfaces();
                string actualInterface = ClientInterfacePlan.ReplayInterfaceForLaunch(
                    launchPatch,
                    launchFile.Root,
                    launchFile.Account
                );
                if (ClientInterfacePlan.LoadsDefaultHud(expectedInterface, actualInterface))
                {
                    if (defaultHudCorrections >= 1)
                    {
                        logger.LogWarning(
                            "The client read replayinterface={Actual} and the HUD stays the default. The replay stops. Battle.net was not clicked.",
                            actualInterface ?? "(missing)"
                        );
                        return new ColdBoot(
                            RetryDisconnect: false,
                            ClientHoldReason.ClientNotReady
                        );
                    }

                    defaultHudCorrections++;
                    checkedLaunchFile = false;
                    logger.LogWarning(
                        "This client reads replayinterface={Actual}. That loads the default HUD. Closing the client and writing {Expected}.",
                        actualInterface ?? "(missing)",
                        expectedInterface
                    );
                    await RestartForObserverInterfaceAsync(replayPath).ConfigureAwait(false);
                    interfaceRestarted = true;
                    openedFromHome = false;
                    openedOnMatchingExe = false;
                    sawGameDataStartup = false;
                    loggedPreparing = false;
                    blankTiming = false;
                    clientAlreadyRunning = false;
                    started = DateTimeOffset.UtcNow;
                    deadline = ClientRelaunch.DeadlineAfterInterfaceRestart(started);
                    continue;
                }
            }
            if (ClientRelaunch.MatchingOpenLostTheBuild(openedOnMatchingExe, differentBuild))
            {
                logger.LogInformation(
                    "The matching client did not stay open. The next replay starts. Battle.net was not clicked."
                );
                return new ColdBoot(RetryDisconnect: false, ClientHoldReason.ClientNotReady);
            }

            if (differentBuild && !loggedHandoff)
            {
                loggedHandoff = true;
                logger.LogInformation(
                    "HeroesSwitcher started a different build. Waiting for the replay's client. Battle.net was not clicked."
                );
            }

            if (
                ClientRelaunch.KeepsWaitingForSwitcherHandoff(
                    openedThroughSwitcher,
                    differentBuild,
                    IsGameProcessRunning()
                )
            )
            {
                DateTimeOffset handoffUntil = ClientInterfacePlan.ExtendForGameDataDownload(
                    started,
                    deadline,
                    DateTimeOffset.UtcNow
                );
                if (handoffUntil > deadline)
                {
                    deadline = handoffUntil;
                }
            }

            if (
                !ClientInterfacePlan.DownloadBelongsToReplayClient(
                    sawGameDataDownload,
                    matchingBuild
                )
            )
            {
                sawGameDataDownload = false;
                loggedDownload = false;
            }

            sawGameDataStartup = ClientInterfacePlan.LatchGameDataStartup(
                sawGameDataStartup,
                startup,
                matchingBuild,
                differentBuild
            );
            bool replayVisible = loading || timer || inMatch || home;
            if (
                ClientInterfacePlan.RestartAfterGameData(
                    sawGameDataDownload,
                    downloadVisible: downloading,
                    startup,
                    replayVisible,
                    dataRestarts,
                    matchingBuild,
                    sawGameDataStartup
                )
            )
            {
                dataRestarts++;
                if (sawGameDataDownload)
                {
                    logger.LogInformation(
                        "Heroes finished downloading game data. Restarting the client so AhliObs loads. Battle.net was not clicked."
                    );
                }
                else
                {
                    logger.LogInformation(
                        "Heroes finished preparing game data. Restarting the client so AhliObs loads. Battle.net was not clicked."
                    );
                }
                await RestartForObserverInterfaceAsync(replayPath).ConfigureAwait(false);
                interfaceRestarted = true;
                openedFromHome = false;
                openedOnMatchingExe = false;
                sawGameDataStartup = false;
                loggedPreparing = false;
                blankTiming = false;
                clientAlreadyRunning = false;
                started = DateTimeOffset.UtcNow;
                deadline = ClientRelaunch.DeadlineAfterInterfaceRestart(started);
                logger.LogInformation(
                    "AhliObs restart opened a new client. The launch wait starts again. Battle.net was not clicked."
                );
                continue;
            }

            bool oweAhliObs = ClientInterfacePlan.OwesObserverRestart(
                matchingBuild,
                sawGameDataStartup,
                startup,
                dataRestarts
            );
            if (
                home
                && !oweAhliObs
                && buildDownload != BuildDownloadState.Waiting
                && ClientInterfacePlan.MayAcceptReplayScreen(!differentBuild, true)
            )
            {
                openedFromHome = true;
                OpenReplayFromHome(replayPath);
            }

            if (!oweAhliObs && ClientInterfacePlan.MayAcceptReplayScreen(!differentBuild, loading))
            {
                ShowGameScene("loading screen in memory");
                return new ColdBoot(RetryDisconnect: false, ClientHoldReason.None);
            }

            if (!oweAhliObs && ClientInterfacePlan.MayAcceptReplayScreen(!differentBuild, timer))
            {
                ShowGameScene("match clock running");
                return new ColdBoot(RetryDisconnect: false, ClientHoldReason.None);
            }

            if (
                !oweAhliObs
                && ClientInterfacePlan.MayAcceptReplayScreen(
                    matchingBuild && !differentBuild,
                    inMatch
                )
            )
            {
                // The session reads the memory clock from here; the HUD timer is never OCR'd.
                replayFileOpened = true;
                replayOnClient ??= replayPath;
                ShowGameScene("match in memory");
                return new ColdBoot(RetryDisconnect: false, ClientHoldReason.None);
            }

            bool blank =
                !startup && ClientRelaunch.IsBlankClientWindow(text, window.Width, window.Height);
            if (!blank)
            {
                blankTiming = false;
            }
            else if (!blankTiming)
            {
                blankTiming = true;
                blankSince = DateTimeOffset.UtcNow;
            }

            TimeSpan blankFor = blankTiming ? DateTimeOffset.UtcNow - blankSince : TimeSpan.Zero;
            bool startupOrDownload = startup || downloading;
            bool gameDataStillStarting = ClientRelaunch.KeepsWaitingForGameData(
                startup,
                sawGameDataStartup,
                blank,
                clientAlreadyRunning,
                matchingBuild,
                blankFor
            );
            if (gameDataStillStarting)
            {
                ServiceHeartbeat.RecordLaunching();
            }

            bool clientBusy =
                loading
                || timer
                || inMatch
                || home
                || startupOrDownload
                || blank
                || differentBuild
                || gameDataStillStarting
                || oweAhliObs;
            if (!boundedWait || clientBusy)
            {
                stuckSince = DateTimeOffset.UtcNow;
            }
            else
            {
                LaunchWaitAction stuck = ReplayClientRoute.DecideStuckWait(
                    launchPatch,
                    DateTimeOffset.UtcNow - stuckSince,
                    LaunchWaitLimit,
                    waitRecoveries
                );
                if (stuck != LaunchWaitAction.KeepWaiting)
                {
                    logger.LogWarning(
                        "Launch wait on replay {Version} saw nothing usable for {Waited} (limit {Limit}). Running client {Running}, memory screen {Screen} ({ScreenReason}, menu seen {MenuSeen}), match clock {ClockReason}, window text: {Text}. Recovery {Action}.",
                        replayVersion,
                        DateTimeOffset.UtcNow - stuckSince,
                        LaunchWaitLimit,
                        runningBuild,
                        screen?.Screen,
                        screen?.Reason,
                        screen?.MenuSeen,
                        lastClockReason,
                        ScreenShadow.Excerpt(text),
                        stuck
                    );
                }

                if (stuck == LaunchWaitAction.GiveUp)
                {
                    logger.LogWarning(
                        "The client was already recovered once for this replay. The launch ends now and the replay stays queued. Battle.net was not clicked."
                    );
                    return new ColdBoot(RetryDisconnect: false, ClientHoldReason.ClientNotReady);
                }

                if (stuck == LaunchWaitAction.RelaunchCurrent)
                {
                    waitRecoveries++;
                    await RestartAuthenticatedClientAsync().ConfigureAwait(false);
                    openedFromHome = false;
                    openedOnMatchingExe = false;
                    blankTiming = false;
                    clientAlreadyRunning = false;
                    sawGameDataStartup = false;
                    loggedPreparing = false;
                    started = DateTimeOffset.UtcNow;
                    stuckSince = started;
                    deadline = ClientRelaunch.DeadlineAfterInterfaceRestart(started);
                    continue;
                }

                if (stuck == LaunchWaitAction.ReopenThroughSwitcher)
                {
                    waitRecoveries++;
                    logger.LogInformation(
                        "Opening the previous-patch replay again through HeroesSwitcher. The client was not closed. Battle.net Play was not used."
                    );
                    CloseIdleSwitcher();
                    replayFileOpened = true;
                    replayOpener.Open(replayPath);
                    NoteSwitcherOpen();
                    replayOnClient = replayPath;
                    openedOnMatchingExe = true;
                    stuckSince = DateTimeOffset.UtcNow;
                    DateTimeOffset reopened = ClientRelaunch.DeadlineAfterInterfaceRestart(
                        stuckSince
                    );
                    if (reopened > deadline)
                    {
                        deadline = reopened;
                    }

                    await Task.Delay(settings.OCR.CheckSleepDuration, tokenProvider.Token)
                        .ConfigureAwait(false);
                    continue;
                }
            }

            if (
                ClientRelaunch.BlankLaunchIsBroken(
                    IsGameProcessRunning(),
                    blank,
                    startupOrDownload,
                    blankFor,
                    matchingBuild
                )
            )
            {
                ReplaySignInRecovery recovery = ReplayClientRoute.Recover(
                    launchPatch,
                    blankRelaunches
                );
                blankRelaunches++;
                if (recovery == ReplaySignInRecovery.OpenPreviousBuild)
                {
                    logger.LogWarning(
                        "Previous-patch window stayed black with no menu and no game-data text. Opening that build again through HeroesSwitcher."
                    );
                    await ReopenPreviousBuildAsync(replayPath).ConfigureAwait(false);
                }
                else if (recovery == ReplaySignInRecovery.LaunchCurrent)
                {
                    logger.LogWarning(
                        "Signed-in client stayed on a black window. Closing it and asking Battle.net to start Heroes again."
                    );
                    await RestartAuthenticatedClientAsync(ClientRelaunch.SettleAfterBrokenWindow)
                        .ConfigureAwait(false);
                }
                else
                {
                    logger.LogWarning(
                        "Signed-in client stayed on a black window. Closing it so the next replay can start. Battle.net was not clicked."
                    );
                    Kill();
                    replayFileOpened = false;
                    return new ColdBoot(RetryDisconnect: false, ClientHoldReason.ClientNotReady);
                }

                openedFromHome = false;
                openedOnMatchingExe = false;
                blankTiming = false;
                clientAlreadyRunning = false;
                sawGameDataStartup = false;
                loggedPreparing = false;
                started = DateTimeOffset.UtcNow;
                deadline = ClientRelaunch.DeadlineAfterInterfaceRestart(started);
                continue;
            }
            if (
                !oweAhliObs
                && ReplayClientRoute.OpenMatchingBuildNow(
                    ReplayClientRoute.Decide(
                        launchPatch,
                        runningBuild,
                        home,
                        replayPresented: false
                    ),
                    openedOnMatchingExe || openedFromHome,
                    blank,
                    gameDataStillStarting
                )
            )
            {
                logger.LogInformation(
                    "Matching Heroes client is up without the home screen or the match clock. Opening the replay on that client. Battle.net was not clicked."
                );
                await OpenOnMatchingClientAsync(replayPath, replayVersion).ConfigureAwait(false);
                openedOnMatchingExe = true;
                clientAlreadyRunning = false;
                sawGameDataStartup = false;
                loggedPreparing = false;
                blankTiming = false;
                started = DateTimeOffset.UtcNow;
                deadline = ClientRelaunch.DeadlineAfterInterfaceRestart(started);
                continue;
            }

            if (gameDataStillStarting)
            {
                DateTimeOffset extended = ClientRelaunch.ExtendForGameDataStartup(
                    started,
                    deadline,
                    DateTimeOffset.UtcNow
                );
                if (extended > deadline)
                {
                    deadline = extended;
                }

                if (!loggedPreparing)
                {
                    loggedPreparing = true;
                    logger.LogInformation(
                        startup || sawGameDataStartup
                            ? "Heroes is preparing game data. The launch wait continues."
                            : "Matching Heroes client is still starting. The launch wait continues."
                    );
                }
            }
            else if (blank)
            {
                if (
                    ClientRelaunch.ShouldRelaunchBlankWindow(
                        IsLaunched(),
                        openedFromHome || openedOnMatchingExe,
                        blank,
                        blankFor,
                        blankRelaunches,
                        !differentBuild
                    )
                )
                {
                    ReplaySignInRecovery recovery = ReplayClientRoute.Recover(
                        launchPatch,
                        blankRelaunches
                    );
                    blankRelaunches++;
                    if (recovery == ReplaySignInRecovery.OpenPreviousBuild)
                    {
                        logger.LogWarning(
                            "Previous-patch window stayed blank. Opening that build again through HeroesSwitcher."
                        );
                        await ReopenPreviousBuildAsync(replayPath).ConfigureAwait(false);
                    }
                    else if (recovery == ReplaySignInRecovery.LaunchCurrent)
                    {
                        logger.LogWarning(
                            "Heroes window stayed blank. Starting the signed-in client again before opening the replay."
                        );
                        await RestartAuthenticatedClientAsync().ConfigureAwait(false);
                    }

                    openedFromHome = false;
                    openedOnMatchingExe = false;
                    blankTiming = false;
                }
            }

            await Task.Delay(settings.OCR.CheckSleepDuration, tokenProvider.Token)
                .ConfigureAwait(false);
        }

        return new ColdBoot(
            RetryDisconnect: false,
            ClientRelaunch.ColdBootHold(
                openedFromHome,
                replayFileOpened,
                sawGameDataStartup,
                interfaceRestarted,
                IsGameProcessRunning(),
                ReadRunningBuild(replayVersion) != RunningClientBuild.Differs
            )
        );
    }

    private bool LaunchFileLoadsDefaultHud(ReplayClientPatch patch, string expected)
    {
        ReplayInterfaceValues values = clientConfigurator.ReadReplayInterfaces();
        string actual = ClientInterfacePlan.ReplayInterfaceForLaunch(
            patch,
            values.Root,
            values.Account
        );
        return ClientInterfacePlan.LoadsDefaultHud(expected, actual);
    }

    private async Task RestartForObserverInterfaceAsync(string replayPath)
    {
        Kill();
        replayFileOpened = false;
        await Task.Delay(ClientRelaunch.SettleAfterExit, tokenProvider.Token).ConfigureAwait(false);
        CloseIdleSwitcher();
        try
        {
            ClientConfigureResult result = clientConfigurator.Configure();
            logger.LogInformation(
                "Applied windowed 1080p, background audio, and AhliObs after the game-data download. Interface copied: {Copied}.",
                result.InterfaceCopied
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not write AhliObs after the game-data download.");
        }

        if (ReplayClientRoute.OpensThroughSwitcher(launchPatch))
        {
            logger.LogInformation(
                "Opening the previous-patch replay again through HeroesSwitcher. Battle.net Play was not used."
            );
            replayFileOpened = true;
            replayOpener.Open(replayPath);
            NoteSwitcherOpen();
            replayOnClient = replayPath;
            return;
        }

        await WaitForAuthenticatedClientAsync().ConfigureAwait(false);
    }

    private async Task RestartAuthenticatedClientAsync(TimeSpan? settle = null)
    {
        Kill();
        replayFileOpened = false;
        await Task.Delay(settle ?? ClientRelaunch.SettleAfterExit, tokenProvider.Token)
            .ConfigureAwait(false);
        CloseIdleSwitcher();
        await WaitForAuthenticatedClientAsync().ConfigureAwait(false);
    }

    private async Task ReopenPreviousBuildAsync(string replayPath)
    {
        Kill();
        replayFileOpened = false;
        await Task.Delay(ClientRelaunch.SettleAfterExit, tokenProvider.Token).ConfigureAwait(false);
        logger.LogInformation(
            "Opening the previous-patch replay again through HeroesSwitcher. Battle.net Play was not used."
        );
        CloseIdleSwitcher();
        replayFileOpened = true;
        replayOpener.Open(replayPath);
        NoteSwitcherOpen();
        replayOnClient = replayPath;
    }

    private async Task OpenOnMatchingClientAsync(string replayPath, string replayVersion)
    {
        string exe = InstalledClientCatalog.FindExe(
            InstalledClientCatalog.Clients(settings.Location?.GameInstallDirectory),
            replayVersion
        );
        if (string.IsNullOrWhiteSpace(exe))
        {
            logger.LogWarning(
                "The matching Heroes client for {Version} is not installed. The replay file was not opened.",
                replayVersion
            );
            return;
        }

        if (IsGameProcessRunning())
        {
            logger.LogInformation(
                "Closing Heroes so the matching client can open the replay. Battle.net was not clicked."
            );
            Kill();
            replayFileOpened = false;
            await Task.Delay(ClientRelaunch.SettleAfterExit, tokenProvider.Token)
                .ConfigureAwait(false);
            if (IsGameProcessRunning())
            {
                logger.LogWarning(
                    "Heroes is still running. The matching client was not started. Battle.net was not clicked."
                );
                return;
            }
        }

        CloseIdleSwitcher();
        replayFileOpened = true;
        replayOpener.OpenMatching(exe, replayPath);
        replayOnClient = replayPath;
    }

    private void CloseIdleSwitcher()
    {
        Process[] switchers = Process.GetProcessesByName(NamedProcess.HeroesSwitcher);
        try
        {
            bool switcherRunning = false;
            foreach (Process switcher in switchers)
            {
                try
                {
                    if (!switcher.HasExited)
                    {
                        switcherRunning = true;
                        break;
                    }
                }
                catch (InvalidOperationException) { }
            }

            if (!ClientRelaunch.ShouldCloseSwitcher(IsGameProcessRunning(), switcherRunning))
            {
                return;
            }

            foreach (Process switcher in switchers)
            {
                try
                {
                    if (!switcher.HasExited)
                    {
                        switcher.Kill(entireProcessTree: true);
                        switcher.WaitForExit(5000);
                    }
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }

            logger.LogInformation(
                "Closing HeroesSwitcher because Heroes is not running. Battle.net was not clicked."
            );
        }
        finally
        {
            foreach (Process switcher in switchers)
            {
                switcher.Dispose();
            }
        }
    }

    private readonly record struct WindowRead(string Text, int Width, int Height);

    private async Task<WindowRead> ReadWindowAsync()
    {
        if (!TryGetGameHandle(out IntPtr handle))
        {
            return new WindowRead(string.Empty, 0, 0);
        }

        using Bitmap frame = capture.Capture(handle);
        if (frame == null)
        {
            return new WindowRead(string.Empty, 0, 0);
        }

        string text = await RecognizeFrameAsync(frame).ConfigureAwait(false);
        logger.LogInformation(
            "Window OCR ({Width}x{Height}): {Text}",
            frame.Width,
            frame.Height,
            string.IsNullOrWhiteSpace(text) ? "(empty)" : text
        );
        return new WindowRead(text, frame.Width, frame.Height);
    }

    private async Task<string> RecognizeFrameAsync(Bitmap frame)
    {
        using SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(frame)
            .ConfigureAwait(false);
        OcrResult result = await ocrEngine.RecognizeAsync(softwareBitmap);
        return result?.Text ?? string.Empty;
    }

    private void ShowGameScene(string reason)
    {
        if (!settings.OBS.Enabled)
        {
            return;
        }

        logger.LogInformation("OBS game-scene ({Reason}).", reason);
        obsController.SwapToGameScene();
    }

    /// <summary>
    /// The memory clock, only while it moves. The menu reads zero, and the last match's clock
    /// can sit frozen until the next one starts, so one read is not a running match. A fresh
    /// cell on a relaunched client is confirmed inside the same probe
    /// (<see cref="MatchClock.ReadRunningAsync"/>).
    /// </summary>
    public Task<TimeSpan?> TryReadRunningMatchClockAsync() =>
        MatchClock.ReadRunningAsync(
            ReadMatchClockSample,
            () => Task.Delay(MatchClock.RunningProbe)
        );

    /// <summary>
    /// Home from memory only (<see cref="HomeScreenCue"/>, #292): the client's own home screen
    /// (<see cref="ClientScreen"/>) when it can tell, else a menu in <see cref="LoadingScreen"/>.
    /// The window is not OCR'd for it.
    /// </summary>
    private bool SeesHome()
    {
        LoadingScreenSample? screen = ReadScreenInMemory();
        ClientScreenSample? client = ReadClientScreen();
        return HomeScreenCue.Sees(client?.OnHome, screen?.OnMenu);
    }

    /// <summary>
    /// Shadow mode (#292): the memory verdict of the HeroesClientSDK menu screens
    /// (<see cref="ClientScreen"/>) next to an OCR verdict the caller already has. It
    /// changes no decision and never throws into the caller.
    /// </summary>
    private void ShadowScreen(ScreenState state, bool ocr, string ocrText)
    {
        if (settings.OCR?.ShadowEnabled == false)
        {
            return;
        }

        try
        {
            ClientScreenSample? screen = ReadClientScreen();
            GameDataWindowSample? window =
                state == ScreenState.GameDataStartup
                    ? GameDataProgressWindow.Read(clientWindows, GetGameProcess()?.Id)
                    : null;
            ShadowObservation observed = screenShadow.Observe(
                state,
                ocr,
                ScreenMemoryVerdicts.For(state, screen, window),
                ocrText,
                ScreenMemoryVerdicts.Describe(state, screen, window)
            );
            if (observed.SaveFrame)
            {
                SaveShadowFrame(state);
            }
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Screen shadow failed for {State}.", state);
        }
    }

    /// <summary>
    /// A fresh frame of the game window when OCR and memory disagree, saved under the replay
    /// context's shadow folder. The OCR frame is already disposed by then.
    /// </summary>
    private void SaveShadowFrame(ScreenState state)
    {
        string directory = context.Current?.Directory?.FullName;
        if (string.IsNullOrWhiteSpace(directory))
        {
            logger.LogDebug(
                "No screen shadow frame for {State}: there is no replay context directory.",
                state
            );
            return;
        }

        if (!TryGetGameHandle(out IntPtr handle))
        {
            logger.LogDebug("No screen shadow frame for {State}: there is no game window.", state);
            return;
        }

        using Bitmap frame = capture.Capture(handle);
        if (frame == null)
        {
            logger.LogDebug(
                "No screen shadow frame for {State}: the capture returned no bitmap.",
                state
            );
            return;
        }

        string folder = Path.Combine(directory, ScreenShadow.FrameFolder);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, ScreenShadow.FrameFileName(state, DateTimeOffset.Now));
        frame.Save(path, ImageFormat.Png);
        logger.LogInformation("Saved the screen shadow frame for {State} to {Path}.", state, path);
    }

    private LoadingScreenSample? ReadScreenInMemory()
    {
        Process process = GetGameProcess();
        if (process == null)
        {
            return null;
        }

        try
        {
            LoadingScreenSample sample = clientProcess.Read(
                process,
                client => loadingScreen.Read(client)
            );
            if (sample.Screen != lastScreen.Screen || sample.MenuSeen != lastScreen.MenuSeen)
            {
                logger.LogInformation(
                    "Client screen in memory is {Screen} ({Reason}, menu seen {MenuSeen}).",
                    sample.Screen,
                    sample.Reason,
                    sample.MenuSeen
                );
                lastScreen = sample;
            }

            return sample;
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not read the client screen from memory.");
            return null;
        }
    }

    /// <summary>
    /// The menu screens by the client's own names (login form, home, loading, score, a match),
    /// logged when they change. Shadow mode only, until a proof lets it decide (#292).
    /// </summary>
    private ClientScreenSample? ReadClientScreen()
    {
        Process process = GetGameProcess();
        if (process == null)
        {
            return null;
        }

        try
        {
            ClientScreenSample sample = clientProcess.Read(
                process,
                client => clientScreens.Read(client)
            );
            if (
                sample.Screen != lastClientScreen.Screen
                || sample.Reason != lastClientScreen.Reason
                || sample.MenuSeen != lastClientScreen.MenuSeen
            )
            {
                logger.LogInformation(
                    "Client menu screen in memory is {Screen} ({Reason}, menu seen {MenuSeen}): {Shown}.",
                    sample.Screen,
                    sample.Reason,
                    sample.MenuSeen,
                    ScreenMemoryVerdicts.Describe(sample)
                );
                lastClientScreen = sample;
            }

            return sample;
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not read the client menu screen from memory.");
            return null;
        }
    }

    private MatchClockSample ReadMatchClockSample()
    {
        Process process = GetGameProcess();
        if (process == null)
        {
            lastClockReason = "no-process";
            return new MatchClockSample(false, lastClockReason);
        }

        try
        {
            MatchClockSample sample = clientProcess.Read(
                process,
                client => matchClock.Read(client)
            );
            lastClockReason = sample.Reason;
            return sample;
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not read the match clock.");
            lastClockReason = "read-failed";
            return new MatchClockSample(false, lastClockReason);
        }
    }

    public async Task<bool> IsReplayPresentedAsync(LoadedReplay replay)
    {
        if (!IsLaunched())
        {
            return false;
        }

        // Memory only (#292): the running match clock, a match, or the map loading screen
        // (LoadingScreen after a menu, else ClientScreen's map panel, before any menu too). A match
        // with no clock yet is the replay on screen, not a client stuck before its menu (#249).
        // When memory cannot tell, the replay is not presented yet; the screen is not OCR'd.
        bool clockRunning = (await TryReadRunningMatchClockAsync().ConfigureAwait(false)).HasValue;
        LoadingScreenSample? screen = ReadScreenInMemory();
        return ReplayLoadCue.PresentedInMemory(clockRunning, screen, ReadClientScreen()) ?? false;
    }

    public async Task<bool> TrySeeEndScreenAsync(bool nearCore)
    {
        try
        {
            string text = await ReadEndScreenTextAsync().ConfigureAwait(false);
            if (text == null)
            {
                return false;
            }

            bool endScreen = MatchEndBanner.IsEnd(text, nearCore);
            ShadowScreen(ScreenState.EndScreen, endScreen, text);
            if (endScreen)
            {
                logger.LogInformation("End-screen OCR saw: {Text}", text.Replace('\n', ' '));
            }

            return endScreen;
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "End-screen OCR failed.");
            return false;
        }
    }

    /// <summary>
    /// Shadow mode only (#292): OCR of the end-screen banner next to memory's MVP read
    /// (<see cref="ClientScreenSample.OnAwards"/>), from the replay's core-death time on. Only the
    /// MVP and award words count (<see cref="MatchEndBanner.EndsLaunchWait"/>), the same screen
    /// memory names. It decides nothing.
    /// </summary>
    public async Task ShadowEndScreenAsync()
    {
        if (settings.OCR?.ShadowEnabled == false)
        {
            return;
        }

        try
        {
            string text = await ReadEndScreenTextAsync().ConfigureAwait(false);
            if (text != null)
            {
                ShadowScreen(ScreenState.EndScreen, MatchEndBanner.EndsLaunchWait(text), text);
            }
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "End-screen shadow OCR failed.");
        }
    }

    /// <summary>The OCR'd banner area of the game window, or null when there is no frame.</summary>
    private async Task<string> ReadEndScreenTextAsync()
    {
        if (!TryGetGameHandle(out IntPtr handle))
        {
            return null;
        }

        using Bitmap frame = capture.Capture(handle);
        if (frame == null || frame.Width < 200 || frame.Height < 200)
        {
            return null;
        }

        var crop = new Rectangle(
            frame.Width / 5,
            frame.Height / 8,
            frame.Width * 3 / 5,
            frame.Height / 3
        );
        using Bitmap region = frame.Clone(crop, frame.PixelFormat);
        using Bitmap resized = region.GetResized(zoom: 2);
        using SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(resized)
            .ConfigureAwait(false);
        OcrResult result = await ocrEngine.RecognizeAsync(softwareBitmap);
        return result?.Text ?? string.Empty;
    }

    private static async Task<SoftwareBitmap> GetSoftwareBitmapAsync(Bitmap bitmap)
    {
        if (bitmap == null)
            throw new ArgumentNullException(nameof(bitmap));

        using (var stream = new InMemoryRandomAccessStream())
        using (Stream netStream = stream.AsStream())
        {
            bitmap.Save(netStream, ImageFormat.Bmp);
            netStream.Flush();
            stream.Seek(0);

            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(
                BitmapDecoder.BmpDecoderId,
                stream
            );
            return await decoder.GetSoftwareBitmapAsync();
        }
    }

    private bool IsHomeScreen() => IsGameProcessRunning() && SeesHome();

    private bool IsGameProcessRunning()
    {
        Process[] processes = Process.GetProcessesByName(settings.Process.HeroesOfTheStorm);
        try
        {
            return processes.Any(process =>
            {
                try
                {
                    return !process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            });
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private async Task<bool> IsMatchClockRunning() =>
        IsLaunched() && (await TryReadRunningMatchClockAsync().ConfigureAwait(false)) != null;

    public void SendFocus(int index)
    {
        if (index < 0 || index >= Keys.Length)
        {
            logger.LogWarning("Focus index {Index} is out of range.", index);
            return;
        }

        SendChord($"focus slot {index} ({Keys[index]})", Keys[index]);
    }

    public void SendPanel(Panel panel)
    {
        int panelIndex = (int)panel;
        if (panelIndex < 0 || panelIndex >= Keys.Length)
        {
            logger.LogWarning("Panel {Panel} is out of range.", panel);
            return;
        }

        SendChord(
            $"panel {panel} (Ctrl+{panelIndex + 1})",
            VirtualKey.VK_CONTROL,
            Keys[panelIndex]
        );
    }

    public void ShowSelectedUnit()
    {
        SendChord(
            "show selected unit (Ctrl+Alt+K)",
            VirtualKey.VK_CONTROL,
            VirtualKey.VK_MENU,
            VirtualKey.VK_K
        );
    }

    private void SendChord(string description, params VirtualKey[] keys)
    {
        lock (controllerLock)
        {
            if (!TryGetGameHandle(out IntPtr handle))
            {
                logger.LogWarning("Could not {Description}; no game window.", description);
                return;
            }

            GameWindowInput.SendKeys(handle, keys, logger);
            logger.LogInformation("Sent {Description} to hwnd {Handle}.", description, handle);
        }
    }

    public void SaveEndScreenshot()
    {
        try
        {
            if (!TryGetGameHandle(out IntPtr handle))
            {
                logger.LogWarning("Could not capture end screenshot; no game window.");
                return;
            }

            string directory = context.Current?.Directory?.FullName;
            if (string.IsNullOrWhiteSpace(directory))
            {
                logger.LogWarning("Could not capture end screenshot; no context directory.");
                return;
            }

            Directory.CreateDirectory(directory);
            using Bitmap bitmap = capture.Capture(handle);
            if (bitmap == null)
            {
                logger.LogWarning("End screenshot capture returned no bitmap.");
                return;
            }

            TimeSpan timer = context.Current.Timer ?? TimeSpan.Zero;
            string name =
                $"end-{((int)timer.TotalHours):D2}-{timer.Minutes:D2}-{timer.Seconds:D2}.png";
            string path = Path.Combine(directory, name);
            bitmap.Save(path, ImageFormat.Png);
            File.Copy(path, Path.Combine(directory, "end.png"), overwrite: true);
            logger.LogInformation(
                "Wrote end screenshot {Path} ({Width}x{Height}) timer={Timer}.",
                path,
                bitmap.Width,
                bitmap.Height,
                timer
            );
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not write end screenshot.");
        }
    }

    public bool IsGameHung()
    {
        if (!TryGetGameHandle(out IntPtr handle) || handle == IntPtr.Zero)
        {
            return false;
        }

        return NativeMethods.IsHungAppWindow(handle);
    }

    public bool IsGameRunning() => IsGameProcessRunning();

    public Process GetGameProcess()
    {
        if (cachedProcess != null)
        {
            try
            {
                if (!cachedProcess.HasExited)
                {
                    return cachedProcess;
                }
            }
            catch (InvalidOperationException) { }
        }

        foreach (Process process in Process.GetProcessesByName(settings.Process.HeroesOfTheStorm))
        {
            try
            {
                if (!process.HasExited)
                {
                    return process;
                }
            }
            catch (InvalidOperationException) { }

            process.Dispose();
        }

        // The game exited: the memory readers' client goes with it (#382).
        clientProcess.Detach();
        return null;
    }

    public void Kill()
    {
        ClearProcessCache();
        clientProcess.Detach();
        replayOnClient = null;
        try
        {
            bool killed = ResilienceRetry
                .WithDelay<bool>(
                    retries: 5,
                    delayForAttempt: ResilienceRetry.ProcessKillDelay,
                    retry: outcome =>
                        ResilienceRetry.Failed(
                            outcome,
                            error =>
                                error
                                    is Win32Exception
                                        or InvalidOperationException
                                        or NotSupportedException,
                            result => result == false
                        ),
                    onRetry: args =>
                    {
                        if (args.Outcome.Exception != null)
                        {
                            logger.LogError(args.Outcome.Exception, "Could not kill game process.");
                        }
                    }
                )
                .Execute(() =>
                {
                    Process[] processes = Process.GetProcessesByName(
                        settings.Process.HeroesOfTheStorm
                    );
                    try
                    {
                        foreach (var process in processes)
                        {
                            using (process)
                            {
                                if (!process.HasExited)
                                {
                                    process.Kill(entireProcessTree: true);
                                    process.WaitForExit(5000);
                                }
                            }
                        }
                    }
                    catch
                    {
                        foreach (var process in processes)
                        {
                            try
                            {
                                process.Dispose();
                            }
                            catch { }
                        }

                        throw;
                    }

                    Process[] remaining = Process.GetProcessesByName(
                        settings.Process.HeroesOfTheStorm
                    );
                    try
                    {
                        return remaining.Length == 0;
                    }
                    finally
                    {
                        foreach (var process in remaining)
                        {
                            process.Dispose();
                        }
                    }
                });

            logger.Log(
                killed ? LogLevel.Information : LogLevel.Error,
                $"Game process killed: {killed}"
            );
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not kill game process.");
        }
    }

    private bool IsLaunched() => TryGetGameHandle(out _);

    private void ClearProcessCache()
    {
        cachedProcess?.Dispose();
        cachedProcess = null;
    }

    // An elevated game blocks memory reads and posted hotkeys from an unelevated spectator.
    private void WarnIfGameIsElevated(int processId)
    {
        if (MediumIntegrityProcess.IsCurrentProcessElevated())
        {
            return;
        }

        if (MediumIntegrityProcess.IsProcessElevated(processId) != false)
        {
            logger.LogWarning(
                "Heroes of the Storm (pid {Pid}) is elevated or its token cannot be read. The memory clock and hotkeys need it to run unelevated. Start Battle.net and Heroes from a normal, unelevated session.",
                processId
            );
        }
    }

    private bool TryGetGameHandle(out IntPtr handle)
    {
        handle = IntPtr.Zero;
        int minWidth = 1280;
        int minHeight = 720;
        if (int.TryParse(settings.Client?.Width, out int configuredWidth) && configuredWidth > 0)
        {
            minWidth = Math.Min(minWidth, configuredWidth);
        }

        if (int.TryParse(settings.Client?.Height, out int configuredHeight) && configuredHeight > 0)
        {
            minHeight = Math.Min(minHeight, configuredHeight);
        }

        if (cachedProcess != null)
        {
            try
            {
                if (!cachedProcess.HasExited)
                {
                    handle = GameWindowInput.FindLargestVisibleWindow(
                        cachedProcess.Id,
                        minWidth,
                        minHeight
                    );
                    if (handle != IntPtr.Zero)
                    {
                        return true;
                    }
                }
            }
            catch (InvalidOperationException) { }

            ClearProcessCache();
        }

        Process[] all = Process.GetProcessesByName(settings.Process.HeroesOfTheStorm);
        try
        {
            foreach (var candidate in all)
            {
                if (cachedProcess == null && !candidate.HasExited)
                {
                    cachedProcess = candidate;
                    WarnIfGameIsElevated(candidate.Id);
                    handle = GameWindowInput.FindLargestVisibleWindow(
                        candidate.Id,
                        minWidth,
                        minHeight
                    );
                }
                else
                {
                    candidate.Dispose();
                }
            }

            return handle != IntPtr.Zero;
        }
        catch
        {
            foreach (var candidate in all)
            {
                if (!ReferenceEquals(candidate, cachedProcess))
                {
                    try
                    {
                        candidate.Dispose();
                    }
                    catch { }
                }
            }

            ClearProcessCache();
            throw;
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool IsHungAppWindow(IntPtr hwnd);
    }
}
