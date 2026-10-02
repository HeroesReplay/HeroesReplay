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
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Replays.Context;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating.Capture;
using HeroesReplay.Core.Spectating.Clock.Memory;
using HeroesReplay.Core.Spectating.Clock.Ocr;
using HeroesReplay.Core.Spectating.Session;
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
    private readonly StableMatchClock matchClock = new();
    private Process cachedProcess;
    private string lastRejectedTimer;
    private bool replayFileOpened;
    private string openedReplayPath;
    private ReplayClientPatch launchPatch = ReplayClientPatch.Current;
    private int? launcherRecoveryReplayId;
    private int launcherRecoveryAttempt;

    public bool ReplayFileOpened => replayFileOpened;

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
        CancellationTokenProvider tokenProvider
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        return await LaunchAndWait().ConfigureAwait(false);
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

    public async Task<bool> OpenReplayFromHomeScreenAsync(string replayPath)
    {
        if (!IsLaunched() || !await IsHomeScreen().ConfigureAwait(false))
        {
            return false;
        }

        OpenReplayFromHome(replayPath);
        return true;
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
        launchPatch = ReplayClientRoute.Classify(replayVersion, installed);
        if (installed.Count == 0)
        {
            logger.LogWarning(
                "No Heroes clients were found under Versions. This replay uses the current-patch sign-in."
            );
        }

        RunningClientBuild running = ReadRunningBuild(replayVersion);
        bool presented = false;
        bool home = false;
        if (running == RunningClientBuild.Matches)
        {
            presented = await IsReplayPresentedAsync(context.Current?.LoadedReplay)
                .ConfigureAwait(false);
            home = !presented && await IsHomeScreen().ConfigureAwait(false);
        }

        ReplayLaunchAuth auth = ReplayClientRoute.Decide(launchPatch, running, home, presented);
        logger.LogInformation(
            "Replay {Version} is the {Patch} patch. Running client is {Running}. Launch step is {Auth}. Installed: {Installed}.",
            string.IsNullOrWhiteSpace(replayVersion) ? "(unknown)" : replayVersion,
            launchPatch,
            running,
            auth,
            installed.Count == 0 ? "(none)" : string.Join(", ", installed)
        );

        if (auth == ReplayLaunchAuth.Unavailable)
        {
            logger.LogWarning(
                "Replay {Version} needs a Heroes client that is not installed. The current patch was not launched.",
                replayVersion
            );
            return new ReplayBoot(auth, false);
        }

        if (auth == ReplayLaunchAuth.AlreadyInMatch)
        {
            replayFileOpened = true;
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
            return new ReplayBoot(ReplayLaunchAuth.Wait, false);
        }

        if (auth == ReplayLaunchAuth.Wait)
        {
            logger.LogInformation(
                running == RunningClientBuild.Unreadable
                    ? "The running Heroes version could not be read. The replay file stays closed."
                    : "Matching current-patch client is up without the home screen or the match clock. The replay file stays closed until the signed-in menu is visible."
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
            logger.LogInformation(
                "Opening previous-patch replay {Version} through HeroesSwitcher. The running client was not closed. Battle.net Play was not used.",
                replayVersion
            );
            CloseIdleSwitcher();
            replayFileOpened = true;
            replayOpener.Open(replayPath);
            return new ReplayBoot(auth, false);
        }

        await WaitForAuthenticatedClientAsync().ConfigureAwait(false);
        if (IsLaunched() && await IsHomeScreen().ConfigureAwait(false))
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

        try
        {
            string version = cachedProcess?.MainModule?.FileVersionInfo?.FileVersion;
            if (string.IsNullOrWhiteSpace(version))
            {
                return RunningClientBuild.Unreadable;
            }

            return ReplayClientRoute.SameBuild(version, replayVersion)
                ? RunningClientBuild.Matches
                : RunningClientBuild.Differs;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not read the running Heroes file version.");
            return RunningClientBuild.Unreadable;
        }
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
        var searchTerms = context
            .Current.LoadedReplay.Replay.Players.Select(x => x.Name)
            .Concat(context.Current.LoadedReplay.Replay.Players.Select(x => x.Character))
            .Concat(settings.OCR.LoadingScreenText)
            .Concat(new[] { context.Current.LoadedReplay.Replay.Map })
            .ToArray();
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

        async Task<bool> HoldForGameDataDownloadAsync(string primary, string later)
        {
            if (!ClientScreenText.IsGameDataDownload(primary, later))
            {
                return false;
            }

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

        while (DateTimeOffset.UtcNow < deadline)
        {
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
            if (BattleNetDisconnect.IsShown(text))
            {
                logger.LogWarning("Battle.net disconnect dialog: {Text}", text);
                return new ColdBoot(RetryDisconnect: true, ClientHoldReason.None);
            }

            ClientHoldReason hold = ClientHold.Classify(text);
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

            if (MatchEndBanner.EndsLaunchWait(text))
            {
                logger.LogInformation("The client is on the award screen. This replay is over.");
                return new ColdBoot(RetryDisconnect: false, ClientHoldReason.AwardScreen);
            }

            if (await HoldForGameDataDownloadAsync(text, null).ConfigureAwait(false))
            {
                continue;
            }

            bool loading = searchTerms.Any(word =>
                !string.IsNullOrWhiteSpace(word)
                && text.Contains(word, StringComparison.OrdinalIgnoreCase)
            );
            bool timer = await IsReplay().ConfigureAwait(false);
            if (!recoveredLogin && !loading && !timer && ClientScreenText.IsLoginForm(text))
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
                continue;
            }

            string laterText = null;
            bool home = false;
            if (!openedFromHome && !loading && !timer && IsGameProcessRunning())
            {
                WordScan homeScan = await ScanPrimaryAsync(settings.OCR.HomeScreenText)
                    .ConfigureAwait(false);
                laterText = homeScan.Text;
                home = homeScan.Found;
            }

            if (await HoldForGameDataDownloadAsync(text, laterText).ConfigureAwait(false))
            {
                continue;
            }

            bool laterLoading =
                !string.IsNullOrWhiteSpace(laterText)
                && searchTerms.Any(word =>
                    !string.IsNullOrWhiteSpace(word)
                    && laterText.Contains(word, StringComparison.OrdinalIgnoreCase)
                );
            bool startup = ClientScreenText.IsGameDataStartup(text, laterText);
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
            bool replayVisible = loading || laterLoading || timer || home;
            if (
                ClientInterfacePlan.RestartAfterGameData(
                    sawGameDataDownload,
                    downloadVisible: ClientScreenText.IsGameDataDownload(text, laterText),
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
                && ClientInterfacePlan.MayAcceptReplayScreen(!differentBuild, true)
            )
            {
                openedFromHome = true;
                OpenReplayFromHome(replayPath);
            }

            if (
                !oweAhliObs
                && ClientInterfacePlan.MayAcceptReplayScreen(
                    !differentBuild,
                    loading || laterLoading
                )
            )
            {
                ShowGameScene("loading screen");
                return new ColdBoot(RetryDisconnect: false, ClientHoldReason.None);
            }

            if (!oweAhliObs && ClientInterfacePlan.MayAcceptReplayScreen(!differentBuild, timer))
            {
                ShowGameScene("timer visible");
                return new ColdBoot(RetryDisconnect: false, ClientHoldReason.None);
            }

            bool blank = ClientRelaunch.IsBlankClientWindow(text, window.Width, window.Height);
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
            bool startupOrDownload =
                startup || ClientScreenText.IsGameDataDownload(text, laterText);
            bool gameDataStillStarting = ClientRelaunch.KeepsWaitingForGameData(
                startup,
                sawGameDataStartup,
                blank,
                clientAlreadyRunning,
                matchingBuild,
                blankFor
            );
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

        if (launchPatch == ReplayClientPatch.Previous)
        {
            logger.LogInformation(
                "Opening the previous-patch replay again through HeroesSwitcher. Battle.net Play was not used."
            );
            replayFileOpened = true;
            replayOpener.Open(replayPath);
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
    }

    private void CloseIdleSwitcher()
    {
        Process[] switchers = Process.GetProcessesByName("HeroesSwitcher_x64");
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

    private readonly record struct WordScan(bool Found, string Text);

    private async Task<string> ReadWindowTextAsync()
    {
        WindowRead window = await ReadWindowAsync().ConfigureAwait(false);
        return window.Text;
    }

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
        if (string.IsNullOrWhiteSpace(text))
        {
            string startup = await StartupTextFromOtherWindowsAsync(handle).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(startup))
            {
                text = startup;
            }
        }

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

    /// <summary>
    /// The largest Heroes window can stay black while a smaller window still says it is
    /// preparing game data. Return that phrase only. Do not log the other window's text.
    /// </summary>
    private async Task<string> StartupTextFromOtherWindowsAsync(IntPtr primary)
    {
        Process process = cachedProcess;
        if (process == null)
        {
            return null;
        }

        int processId;
        try
        {
            if (process.HasExited)
            {
                return null;
            }

            processId = process.Id;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }

        var windows = new List<GameWindowInput.VisibleClientWindow>();
        GameWindowInput.CollectVisibleWindows(processId, windows);
        GameWindowInput.CollectVisibleChildWindows(primary, windows);
        foreach (GameWindowInput.VisibleClientWindow window in windows)
        {
            if (window.Handle == primary || window.Handle == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                using Bitmap frame = capture.Capture(window.Handle);
                if (frame == null)
                {
                    continue;
                }

                string text = await RecognizeFrameAsync(frame).ConfigureAwait(false);
                if (
                    ClientScreenText.IsGameDataStartup(text)
                    || ClientScreenText.IsGameDataDownload(text)
                )
                {
                    return text;
                }
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "Could not read another Heroes window.");
            }
        }

        return null;
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

    public TimeSpan? TryReadMatchClock()
    {
        Process process = GetGameProcess();
        if (process == null)
        {
            return null;
        }

        try
        {
            if (!matchClock.TryRead(process, out TimeSpan time))
            {
                return null;
            }

            return time;
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not read the match clock.");
            return null;
        }
    }

    public async Task<TimeSpan?> TryGetTimerAsync()
    {
        try
        {
            using (Bitmap timerBitmap = GetNegativeOffsetTimer())
            {
                if (timerBitmap == null)
                    return null;

                if (settings.Capture.SaveTimerRegion)
                {
                    timerBitmap.Save(
                        Path.Combine(
                            settings.CapturesPath,
                            "timer-" + Guid.NewGuid().ToString() + ".bmp"
                        )
                    );
                }

                return await ConvertBitmapTimerToTimeSpan(timerBitmap).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not get timer from bitmap");
        }

        return null;
    }

    public async Task<bool> IsReplayPresentedAsync(LoadedReplay replay)
    {
        if (!IsLaunched())
        {
            return false;
        }

        var parsed = replay?.Replay;
        string text = await ReadWindowTextAsync().ConfigureAwait(false);
        if (
            ReplayLoadCue.SeesLoadingScreen(
                text,
                parsed?.Map,
                parsed?.MapAlternativeName,
                parsed?.Players?.Select(player => player.Name),
                parsed?.Players?.Select(player => player.Character),
                settings.OCR.LoadingScreenText
            )
        )
        {
            return true;
        }

        // HUD crop only. The memory clock stays unused until the map and this timer are on screen.
        return (await TryGetTimerAsync().ConfigureAwait(false)).HasValue;
    }

    public async Task<bool> TrySeeEndScreenAsync(bool nearCore)
    {
        try
        {
            if (!TryGetGameHandle(out IntPtr handle))
            {
                return false;
            }

            using Bitmap frame = capture.Capture(handle);
            if (frame == null || frame.Width < 200 || frame.Height < 200)
            {
                return false;
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
            string text = result?.Text ?? string.Empty;
            bool endScreen = MatchEndBanner.IsEnd(text, nearCore);
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

    private async Task<TimeSpan?> ConvertBitmapTimerToTimeSpan(Bitmap bitmap)
    {
        using (Bitmap resized = bitmap.GetResized(zoom: 4))
        {
            using (
                SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(resized)
                    .ConfigureAwait(false)
            )
            {
                OcrResult ocrResult = await ocrEngine.RecognizeAsync(softwareBitmap);
                TimeSpan? timer = TryParseTimeSpan(ocrResult.Text);

                if (timer.HasValue)
                    return timer;

                try
                {
                    Directory.CreateDirectory(settings.CapturesPath);
                    resized.Save(
                        Path.Combine(settings.CapturesPath, "timer-rejected.png"),
                        ImageFormat.Png
                    );
                }
                catch (Exception saveError)
                {
                    logger.LogDebug(saveError, "Could not save the rejected timer crop.");
                }

                if (settings.Capture.SaveCaptureFailureCondition)
                {
                    Directory.CreateDirectory(settings.CapturesPath);
                    resized.Save(
                        Path.Combine(settings.CapturesPath, Guid.NewGuid().ToString() + ".bmp")
                    );
                }

                return null;
            }
        }
    }

    private Bitmap GetNegativeOffsetTimer()
    {
        if (!TryGetGameHandle(out IntPtr handle))
        {
            return null;
        }

        Rectangle dimensions = capture.GetClientSize(handle);
        Rectangle crop = HudTimerCrop.ForClient(dimensions.Width, dimensions.Height);
        if (crop.Width <= 0 || crop.Height <= 0)
        {
            return null;
        }

        return capture.Capture(handle, crop);
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

    private TimeSpan? TryParseTimeSpan(string text)
    {
        try
        {
            if (!HudClock.TryParse(text, out TimeSpan clock))
            {
                string sanitized = HudClock.Sanitize(text);
                if (
                    !string.IsNullOrEmpty(sanitized)
                    && !string.Equals(sanitized, lastRejectedTimer, StringComparison.Ordinal)
                )
                {
                    lastRejectedTimer = sanitized;
                    logger.LogWarning(
                        "Timer OCR is not -MM:SS or MM:SS. Saw \"{Text}\", sanitized to \"{Sanitized}\".",
                        text,
                        sanitized
                    );
                }

                return null;
            }

            return clock;
        }
        catch (Exception)
        {
            logger.LogDebug("Could not parse the timer: {Text}", text ?? string.Empty);
        }

        return null;
    }

    private async Task<bool> IsHomeScreen()
    {
        if (!IsGameProcessRunning())
        {
            return false;
        }

        WordScan scan = await ScanPrimaryAsync(settings.OCR.HomeScreenText).ConfigureAwait(false);
        return scan.Found;
    }

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

    private async Task<bool> IsReplay() =>
        IsLaunched() && (await TryGetTimerAsync().ConfigureAwait(false)) != null;

    private async Task<WordScan> ScanPrimaryAsync(IEnumerable<string> words)
    {
        if (!TryGetGameHandle(out IntPtr handle))
        {
            return new WordScan(false, string.Empty);
        }

        using Bitmap frame = capture.Capture(handle);
        if (frame == null)
        {
            return new WordScan(false, string.Empty);
        }

        using SoftwareBitmap softwareBitmap = await GetSoftwareBitmapAsync(frame)
            .ConfigureAwait(false);
        OcrResult result = await ocrEngine.RecognizeAsync(softwareBitmap);
        string recognized = result?.Text ?? string.Empty;
        logger.LogInformation(
            "Window OCR ({Width}x{Height}): {Text}",
            frame.Width,
            frame.Height,
            string.IsNullOrWhiteSpace(recognized) ? "(empty)" : recognized
        );

        if (words != null)
        {
            foreach (string word in words)
            {
                if (
                    !string.IsNullOrWhiteSpace(word)
                    && recognized.Contains(word, StringComparison.OrdinalIgnoreCase)
                )
                {
                    logger.LogInformation("{Word} has been found.", word);
                    return new WordScan(true, recognized);
                }
            }
        }

        if (settings.Capture.SaveCaptureFailureCondition)
        {
            Directory.CreateDirectory(settings.CapturesPath);
            frame.Save(Path.Combine(settings.CapturesPath, Guid.NewGuid().ToString() + ".bmp"));
        }

        return new WordScan(false, recognized);
    }

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

        return null;
    }

    public void Kill()
    {
        ClearProcessCache();
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
