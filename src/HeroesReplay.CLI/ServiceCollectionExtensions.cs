using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using HeroesReplay.CLI.Commands.Update;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.Core;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Analysis.Calculators;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Core.Dependencies;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.GameClient.Firewall;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Obs.Inspection;
using HeroesReplay.Core.Obs.Recording;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Replays.Context;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.SelfUpdate;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.ServiceHost.Logs;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating;
using HeroesReplay.Core.Spectating.Capture;
using HeroesReplay.Core.Spectating.Clock;
using HeroesReplay.Core.Spectating.Control;
using HeroesReplay.Core.Spectating.Reports;
using HeroesReplay.Core.Spectating.Session;
using HeroesReplay.Core.Status;
using HeroesReplay.Core.Twitch;
using HeroesReplay.Core.Twitch.ChatMessages;
using HeroesReplay.Core.Twitch.Predictions;
using HeroesReplay.Core.Twitch.RedeemedRewards;
using HeroesReplay.Core.Twitch.Rewards;
using HeroesReplay.Core.TwitchExtension;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Playlists;
using HeroesReplay.Core.YouTube.Search;
using HeroesReplay.HeroesProfile.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OBSWebsocketDotNet;
using Polly.Telemetry;
using TwitchLib.Api;
using TwitchLib.Api.Core;
using TwitchLib.Api.Core.Interfaces;
using TwitchLib.Api.Interfaces;
using TwitchLib.Client;
using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Models;
using TwitchLib.PubSub;
using TwitchLib.PubSub.Interfaces;

namespace HeroesReplay.CLI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddYouTubeServices(
        this IServiceCollection services,
        CancellationToken token
    )
    {
        IConfigurationRoot configuration = GetConfiguration();

        return services
            .AddHeroesReplayOpenTelemetry(configuration, "heroesreplay-youtube")
            .AddMemoryCache()
            .AddLogging(builder =>
                builder
                    .AddConfiguration(configuration.GetSection("Logging"))
                    .AddConsole()
                    .AddSafeEventLog("HeroesReplay.YouTubeService")
            )
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<IYouTubeUploader, YouTubeUploader>()
            .AddSingleton<IYouTubePlaylistClient, GoogleYouTubePlaylistClient>()
            .AddSingleton<IYouTubeLibrary, YouTubeLibrary>()
            .AddSingleton(new CancellationTokenProvider(token))
            .AddHeroesProfileService()
            .AddSingleton(serviceProvider =>
                BindSettings(serviceProvider.GetRequiredService<IConfiguration>())
            )
            .AddSingleton(CancellationTokenSource.CreateLinkedTokenSource(token))
            .AddSingleton<IConfiguration>(configuration);
    }

    public static IServiceCollection AddReportServices(
        this IServiceCollection services,
        CancellationToken token,
        Type replayProvider,
        ReplayPathOptions replayPath = null
    )
    {
        IConfigurationRoot configuration = GetConfiguration();
        services.AddSingleton(replayPath ?? new ReplayPathOptions());

        var settings = BindSettings(configuration);

        return services
            .AddHeroesReplayOpenTelemetry(configuration, "heroesreplay-calculators")
            .AddLogging(builder =>
                builder
                    .AddConfiguration(configuration.GetSection("Logging"))
                    .AddConsole()
                    .AddSafeEventLog("HeroesReplay.ReportService")
            )
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(settings)
            .AddSingleton(new CancellationTokenProvider(token))
            .AddSingleton<IGameData, GameData>()
            .AddSingleton<IReplayHelper, ReplayHelper>()
            .AddSingleton<IAbilityDetector, AbilityDetector>()
            .AddSingleton<IExtensionPayloadsBuilder, ExtensionPayloadBuilder>()
            .AddSingleton<IReplayAnalyzer, ReplayAnalyzer>()
            .AddSingleton<IReplayLoader, ReplayLoader>()
            .AddSingleton<IContextFileManager, ContextFileManager>()
            .AddSingleton(typeof(IReplayProvider), replayProvider)
            .AddSingleton<ISpectateReportWriter, SpectateReportCsvWriter>()
            .AddFocusCalculators();
    }

    public static IServiceCollection AddCheckServices(
        this IServiceCollection services,
        CancellationToken token
    )
    {
        IConfigurationRoot configuration = GetConfiguration();
        AppSettings settings = BindSettings(configuration);

        // Logs go to stderr: stdout carries the result (check --output json) or, under
        // heroesreplay mcp, the JSON-RPC stream the check_* tools answer on.
        return services
            .AddHeroesReplayOpenTelemetry(configuration, "heroesreplay-check")
            .AddMemoryCache()
            .AddLogging(builder =>
                builder
                    .AddConfiguration(configuration.GetSection("Logging"))
                    .AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace)
            )
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(settings)
            .AddSingleton<StormClientConfigurator>()
            .AddSingleton(new CancellationTokenProvider(token))
            .AddHeroesProfileService()
            .AddTwitchExtensionClient()
            .AddSingleton<OBSWebsocket>()
            .AddSingleton<ITwitchAPI, TwitchAPI>()
            .AddSingleton<IApiSettings>(serviceProvider =>
            {
                AppSettings bound = serviceProvider.GetRequiredService<AppSettings>();
                return new ApiSettings
                {
                    AccessToken = bound.Twitch?.AccessToken,
                    ClientId = bound.Twitch?.ClientId,
                };
            })
            .AddSingleton<SpectatorStatusStore>()
            .AddConnectivityServices();
    }

    public static AppSettings LoadAppSettings()
    {
        return BindSettings(GetConfiguration());
    }

    /// <summary>
    /// The effective settings without resolving secrets or the media policy, for offline
    /// commands that only read local files such as the hero catalog.
    /// </summary>
    public static AppSettings LoadOfflineSettings() =>
        GetConfiguration().Get<AppSettings>() ?? new AppSettings();

    /// <summary>
    /// The effective <c>OBS</c> section without resolving secrets, for commands that only need
    /// names and flags.
    /// </summary>
    public static OBSSettings LoadObsSettings() =>
        GetConfiguration().GetSection("OBS").Get<OBSSettings>() ?? new OBSSettings();

    /// <summary>
    /// The effective <c>ServiceHealth</c> section, for <c>services status</c>. No secret is resolved.
    /// </summary>
    public static ServiceHealthSettings LoadServiceHealthSettings() =>
        GetConfiguration().GetSection("ServiceHealth").Get<ServiceHealthSettings>()
        ?? new ServiceHealthSettings();

    /// <summary>The effective <c>ServiceLogs</c> section. No secret is resolved.</summary>
    public static ServiceLogSettings LoadServiceLogSettings() =>
        GetConfiguration().GetSection("ServiceLogs").Get<ServiceLogSettings>()
        ?? new ServiceLogSettings();

    /// <summary>The effective <c>MachineHealth</c> section. No secret is resolved.</summary>
    public static MachineHealthSettings LoadMachineHealthSettings() =>
        GetConfiguration().GetSection("MachineHealth").Get<MachineHealthSettings>()
        ?? new MachineHealthSettings();

    /// <summary>
    /// The effective <c>Clips</c> and <c>Dependencies</c> sections, for <c>deps install</c> and
    /// <c>check ffmpeg</c>. No secret is resolved.
    /// </summary>
    public static (ClipSettings Clips, DependencySettings Dependencies) LoadToolSettings()
    {
        IConfigurationRoot configuration = GetConfiguration();
        return (
            configuration.GetSection("Clips").Get<ClipSettings>() ?? new ClipSettings(),
            configuration.GetSection("Dependencies").Get<DependencySettings>()
                ?? new DependencySettings()
        );
    }

    /// <summary>The effective <c>ServiceRestart</c> section. No secret is resolved.</summary>
    public static ServiceRestartSettings LoadServiceRestartSettings() =>
        GetConfiguration().GetSection("ServiceRestart").Get<ServiceRestartSettings>()
        ?? new ServiceRestartSettings();

    /// <summary>
    /// Writes this process's logs to <c>&lt;role&gt;-&lt;date&gt;.log</c> under
    /// <c>ServiceLogs:Directory</c>. Does nothing without a role or when the section is disabled.
    /// </summary>
    public static IServiceCollection AddServiceRoleLog(
        this IServiceCollection services,
        string role
    )
    {
        if (!ServiceRoleLog.IsSafeRole(role))
        {
            return services;
        }

        ServiceLogSettings settings = LoadServiceLogSettings();
        if (!settings.Enabled)
        {
            return services;
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, ServiceRoleLogProvider>(
                _ => new ServiceRoleLogProvider(role, settings)
            )
        );
        return services;
    }

    /// <summary>Console, the supervisor's own log file, and OpenTelemetry for the supervisor.</summary>
    public static IServiceCollection AddSupervisorServices(this IServiceCollection services)
    {
        IConfigurationRoot configuration = GetConfiguration();
        return services
            .AddHeroesReplayOpenTelemetry(configuration, "heroesreplay-supervisor")
            .AddLogging(builder =>
                builder.AddConfiguration(configuration.GetSection("Logging")).AddConsole()
            )
            .AddServiceRoleLog(ServiceRoleLog.SupervisorRole);
    }

    /// <summary>
    /// What the read-only OBS MCP tools need, read on each call. No secret is resolved here;
    /// the tools resolve only <c>OBS:WebSocketPassword</c>.
    /// </summary>
    public static ObsInspectionSettings LoadObsInspectionSettings()
    {
        IConfigurationRoot configuration = GetConfiguration();
        var arm = new ObsStreamArm();
        return new ObsInspectionSettings(
            configuration.GetSection("OBS").Get<OBSSettings>() ?? new OBSSettings(),
            AppContext.BaseDirectory,
            configuration.GetSection("Location").Get<LocationSettings>()?.DataDirectory,
            arm.IsArmed(),
            arm.FilePath
        )
        {
            AssetStoreRoot = ObsAssetStore.For(ObsManagedFiles.ForThisUser()).Root,
        };
    }

    public static AppSettings BindSettings(IConfiguration configuration)
    {
        ReplayMediaPolicySettings media = ReplayMediaPolicyStartup.Require(configuration);
        AppSettings settings =
            configuration.Get<AppSettings>()
            ?? throw new InvalidOperationException(
                "Could not bind AppSettings from configuration."
            );
        settings.ReplayMedia = media;
        SecretResolver.Apply(settings);
        return settings;
    }

    public static IServiceCollection AddClientServices(this IServiceCollection services)
    {
        IConfigurationRoot configuration = GetConfiguration();
        AppSettings settings = BindSettings(configuration);
        return services
            .AddHeroesReplayOpenTelemetry(configuration, "heroesreplay-client")
            .AddSingleton(settings)
            .AddSingleton<StormClientConfigurator>()
            .AddSingleton<IGameFirewall, NetshGameFirewall>();
    }

    public static IServiceCollection AddFocusCalculators(this IServiceCollection services)
    {
        return services
            .AddSingleton<IFocusCalculator, KillCalculator>()
            .AddSingleton<IFocusCalculator, DeathCalculator>()
            .AddSingleton<IFocusCalculator, NearEnemyCalculator>()
            .AddSingleton<IFocusCalculator, RoamingCalculator>()
            .AddSingleton<IFocusCalculator, NearCaptureBeaconCalculator>()
            .AddSingleton<IFocusCalculator, NearEnemyCoreCalculator>()
            .AddSingleton<IFocusCalculator, NearBossCalculator>()
            .AddSingleton<IFocusCalculator, CampCaptureCalculator>()
            .AddSingleton<IFocusCalculator, BossCampCaptureCalculator>()
            .AddSingleton<IFocusCalculator, CampClearCalculator>()
            .AddSingleton<IFocusCalculator, MapObjectiveCalculator>()
            .AddSingleton<IFocusCalculator, NearMapUnitCalculator>()
            .AddSingleton<IFocusCalculator, DestroyingStructureCalculator>()
            .AddSingleton<IFocusCalculator, VehicleCalculator>()
            .AddSingleton<IFocusCalculator, EmotingCalculator>();
    }

    public static IServiceCollection AddTwitchServices(
        this IServiceCollection services,
        CancellationToken token,
        string telemetryServiceName = "heroesreplay-twitch"
    )
    {
        IConfigurationRoot configuration = GetConfiguration();
        AppSettings settings = BindSettings(configuration);

        var rewardHandler = typeof(IRewardHandler);
        var rewardHandlerTypes = rewardHandler
            .Assembly.GetTypes()
            .Where(type => type.IsClass && rewardHandler.IsAssignableFrom(type));

        foreach (var type in rewardHandlerTypes)
        {
            services.AddSingleton(rewardHandler, type);
        }

        var commandHandler = typeof(IMessageHandler);
        var commandHandlerTypes = rewardHandler
            .Assembly.GetTypes()
            .Where(type => type.IsClass && commandHandler.IsAssignableFrom(type));

        foreach (var type in commandHandlerTypes)
        {
            services.AddSingleton(commandHandler, type);
        }

        return services
            .AddHeroesReplayOpenTelemetry(configuration, telemetryServiceName)
            .AddMemoryCache()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(settings)
            .AddSingleton<IObserverPanelRequests, ObserverPanelRequests>()
            .AddSingleton(new CancellationTokenProvider(token))
            .AddLogging(builder =>
                builder
                    .AddConfiguration(configuration.GetSection("Logging"))
                    .AddConsole()
                    .AddSafeEventLog("HeroesReplay.TwitchService")
            )
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(settings)
            .AddSingleton(
                typeof(ITwitchBot),
                settings.Capture.Method switch
                {
                    CaptureMethod.None => typeof(FakeTwitchBot),
                    _ => typeof(TwitchBot),
                }
            )
            .AddSingleton(
                typeof(ITwitchClient),
                settings.Capture.Method switch
                {
                    CaptureMethod.None => typeof(FakeTwitchClient),
                    _ => typeof(TwitchClient),
                }
            )
            .AddSingleton(new CancellationTokenProvider(token))
            .AddSingleton<SpectatorStatusStore>()
            .AddSingleton<IRedemptionStatusClient, HelixRedemptionStatus>()
            .AddSingleton<IRedemptionCanceller, RedemptionCanceller>()
            .AddSingleton<RedemptionFulfiller>()
            .AddMatchPredictionServices()
            .AddSingleton<StatusPredictionWatcher>()
            .AddSingleton<ITwitchRewardsManager, TwitchRewardsManager>()
            .AddSingleton<IGameData, GameData>()
            .AddSingleton<ITwitchAPI, TwitchAPI>()
            .AddHeroesProfileService()
            .AddSingleton<ITwitchPubSub, TwitchPubSub>()
            .AddSingleton<ITwitchAPI, TwitchAPI>()
            .AddSingleton<EventSubRewardListener>()
            .AddSingleton(serviceProvider =>
            {
                AppSettings settings = serviceProvider.GetRequiredService<AppSettings>();
                return new ConnectionCredentials(
                    settings.Twitch.Account,
                    settings.Twitch.AccessToken,
                    TwitchChatEndpoint.SecureWebSocket
                );
            })
            .AddSingleton<IApiSettings>(serviceProvider =>
            {
                AppSettings settings = serviceProvider.GetRequiredService<AppSettings>();
                return new ApiSettings
                {
                    AccessToken = settings.Twitch.AccessToken,
                    ClientId = settings.Twitch.ClientId,
                };
            })
            .AddSingleton<ICustomRewardsHolder, SupportedRewardsHolder>()
            .AddSingleton<IOnRewardHandler, OnRewardRedeemedHandler>()
            .AddSingleton<IOnMessageHandler, OnMessageReceivedHandler>()
            .AddSingleton<IRewardRequestFactory, RewardRequestFactory>()
            .AddSingleton<IRequestQueue, RequestQueue>()
            .AddSingleton<IHeroesProfileResume>(_ => new HeroesProfileResume(
                HeroesProfileResume.SharedPath
            ));
    }

    /// <summary>
    /// The Helix prediction service and what it needs. <c>twitch connect</c> and
    /// <c>twitch predictions test</c> both register it here, so the test command cannot miss a
    /// dependency the live host has (#297).
    /// </summary>
    public static IServiceCollection AddMatchPredictionServices(this IServiceCollection services)
    {
        return services
            .AddSingleton<PredictionReportWriter>()
            .AddSingleton<IMatchPredictionService, TwitchMatchPredictionService>();
    }

    /// <summary>
    /// The services <c>spectate</c> resolves. There is no chat bot, chat client, or reward
    /// handler here: chat and channel-point redemptions belong to the twitch role
    /// (<c>twitch connect</c>), so spectate cannot post to chat or answer a redemption (#301).
    /// </summary>
    public static IServiceCollection AddSpectateServices(
        this IServiceCollection services,
        CancellationToken token,
        Type replayProvider,
        ReplayPathOptions replayPath = null
    )
    {
        IConfigurationRoot configuration = GetConfiguration();
        AppSettings settings = BindSettings(configuration);
        services.AddSingleton(replayPath ?? new ReplayPathOptions());

        return services
            .AddHeroesReplayOpenTelemetry(configuration, "heroesreplay-spectate")
            .AddMemoryCache()
            .AddLogging(builder =>
                builder
                    .AddConfiguration(configuration.GetSection("Logging"))
                    .AddConsole()
                    .AddSafeEventLog("HeroesReplay.SpectatorService")
            )
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(settings)
            .AddSingleton(new CancellationTokenProvider(token))
            .AddSingleton(
                typeof(IGameCapture),
                settings.Capture.Method switch
                {
                    CaptureMethod.None => typeof(StubCapture),
                    CaptureMethod.BitBlt => typeof(BitBltCapture),
                    _ => typeof(PrintWindowCapture),
                }
            )
            .AddSingleton(
                typeof(IGameTimer),
                settings.Capture.Method switch
                {
                    CaptureMethod.None => typeof(StubGameTimer),
                    _ => typeof(StableGameTimer),
                }
            )
            .AddSingleton<GameTimerLog>()
            // One attached client per Heroes process for the timer and the controller (#382).
            .AddSingleton<SharedClientProcess>()
            .AddSingleton(
                typeof(IGameController),
                settings.Capture.Method switch
                {
                    CaptureMethod.None => typeof(StubController),
                    _ => typeof(GameController),
                }
            )
            .AddSingleton(
                typeof(ITalentNotifier),
                settings.Capture.Method switch
                {
                    CaptureMethod.None => typeof(StubNotifier),
                    _ => typeof(TalentNotifier),
                }
            )
            .AddSingleton(typeof(IReplayProvider), replayProvider)
            .AddSingleton<IGameData, GameData>()
            .AddSingleton<IReplayHelper, ReplayHelper>()
            .AddSingleton<IAbilityDetector, AbilityDetector>()
            .AddSingleton<IYouTubeReplayLookup, YouTubeReplayLookup>()
            .AddSingleton<RecordingClock>()
            .AddSingleton<IGameFirewall, NetshGameFirewall>()
            .AddSingleton<BattleNetAgentReaper>()
            .AddSingleton<OrphanRecordingOnStart>()
            .AddSingleton<IReplayOpener, MediumIntegrityReplayOpener>()
            .AddSingleton(serviceProvider => new MediaPolicyAttemptLog(
                MediaPolicyAttemptLog.AttemptsRoot(settings),
                serviceProvider.GetRequiredService<ILogger<MediaPolicyAttemptLog>>()
            ))
            .AddSingleton<IGameManager, GameManager>()
            .AddSingleton<IReplayAnalyzer, ReplayAnalyzer>()
            .AddSingleton<IObserverPanelRequests, ObserverPanelRequests>()
            .AddSingleton<StormClientConfigurator>()
            .AddSingleton<ISpectator, Spectator>()
            .AddSingleton<IReplayLoader, ReplayLoader>()
            .AddSingleton<ReplayContext>()
            .AddSingleton<IReplayContextSetter>(provider =>
                provider.GetRequiredService<ReplayContext>()
            )
            .AddSingleton<IReplayContext>(provider => provider.GetRequiredService<ReplayContext>())
            .AddTwitchExtensionClient()
            .AddHeroesProfileService()
            .AddSingleton<IExtensionPayloadsBuilder, ExtensionPayloadBuilder>()
            .AddSingleton<IContextFileManager, ContextFileManager>()
            .AddSingleton<OBSWebsocket>()
            .AddSingleton<IObsController, ObsController>()
            .AddSingleton<IReleaseUpdateGate, ReleaseUpdateGate>()
            .AddSingleton<SpectateMemoryLog>()
            .AddSingleton<IEngine, Engine>()
            .AddSingleton<SpectatorStatusStore>()
            .AddConnectivityServices()
            .AddFocusCalculators();
    }

    // A refused Event Log write must not throw out of a logger and fail a constructor that logs.
    private static ILoggingBuilder AddSafeEventLog(this ILoggingBuilder builder, string sourceName)
    {
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, SafeEventLogProvider>(
                _ => new SafeEventLogProvider(sourceName)
            )
        );
        return builder;
    }

    private static IServiceCollection AddConnectivityServices(this IServiceCollection services)
    {
        services.AddSingleton<IHeroesProfileResume>(_ => new HeroesProfileResume(
            HeroesProfileResume.SharedPath
        ));
        services.AddSingleton<IReplayResume>(_ => new ReplayResumeFile(
            ReplayResumeFile.SharedPath
        ));
        services
            .AddHttpClient<INetworkProbe, NetworkProbe>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(8);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "HeroesReplay-ConnectivityWatchdog"
                );
            })
            .Services.AddSingleton<IConnectivityWatchdog>(sp => new ConnectivityWatchdog(
                sp.GetRequiredService<ILogger<ConnectivityWatchdog>>(),
                sp.GetRequiredService<AppSettings>(),
                sp.GetRequiredService<INetworkProbe>(),
                sp.GetRequiredService<SpectatorStatusStore>(),
                sp.GetRequiredService<CancellationTokenProvider>(),
                sp.GetService<IObsController>(),
                sp.GetRequiredService<IHeroesProfileResume>(),
                sp.GetService<IReplayResume>(),
                () => NamedProcess.IsRunning(NamedProcess.HeroesOfTheStorm)
            ));
        return services;
    }

    private static IServiceCollection AddTwitchExtensionClient(this IServiceCollection services)
    {
        services.AddHttpClient(TwitchExtensionService.HttpClientName);
        services.AddSingleton<ITwitchExtensionService>(serviceProvider =>
            ActivatorUtilities.CreateInstance<TwitchExtensionService>(
                serviceProvider,
                serviceProvider
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(TwitchExtensionService.HttpClientName)
            )
        );
        return services;
    }

    public static IServiceCollection AddHeroesProfileService(this IServiceCollection services)
    {
        return services
            .AddHeroesProfileKiotaClient()
            .AddSingleton<IHeroesProfileService, HeroesProfileService>();
    }

    /// <summary>
    /// The hero statistics refresh behind YouTube title hooks. Its own Heroes Profile client and
    /// <c>HttpClient</c> carry no retry handler, so the refresh decides each wait itself
    /// (<c>Retry-After</c> on a 429, at most 5 times) instead of the replay pipeline's 10 retries.
    /// </summary>
    public static IServiceCollection AddHeroStatsRefresh(this IServiceCollection services)
    {
        services.AddHttpClient(
            HeroStatsApi.HttpClientName,
            client => client.Timeout = HeroesProfileHttp.AttemptTimeout
        );
        return services.AddSingleton(sp =>
        {
            AppSettings settings = sp.GetRequiredService<AppSettings>();
            HeroesProfileApiSettings api = settings.HeroesProfileApi;
            HttpClient httpClient = sp.GetRequiredService<IHttpClientFactory>()
                .CreateClient(HeroStatsApi.HttpClientName);
            HeroesProfileClient client = HeroesProfileClientFactory.Create(
                api?.ApiKey,
                httpClient,
                api?.ExternalV1BaseUri
            );
            return new HeroStatsRefresh(
                new HeroStatsApi(client),
                new HeroStatsStore(settings.Location.DataDirectory),
                api?.HeroStats ?? new HeroStatsSettings(),
                sp.GetRequiredService<ILogger<HeroStatsRefresh>>()
            );
        });
    }

    private static IServiceCollection AddHeroesProfileKiotaClient(this IServiceCollection services)
    {
        services
            .AddHttpClient(
                HeroesProfileHttp.ClientName,
                client => client.Timeout = Timeout.InfiniteTimeSpan
            )
            .AddResilienceHandler(HeroesProfileHttp.ClientName, HeroesProfileHttp.Configure);
        services.Configure<TelemetryOptions>(options =>
            options.SeverityProvider = HeroesProfileHttp.Severity
        );

        return services.AddSingleton(sp =>
        {
            HeroesProfileApiSettings api = sp.GetRequiredService<AppSettings>().HeroesProfileApi;
            HttpClient httpClient = sp.GetRequiredService<IHttpClientFactory>()
                .CreateClient(HeroesProfileHttp.ClientName);
            return HeroesProfileClientFactory.Create(api.ApiKey, httpClient, api.ExternalV1BaseUri);
        });
    }

    private static IConfigurationRoot GetConfiguration() =>
        BuildConfiguration(
            DefaultBasePath(),
            Environment.GetEnvironmentVariable(EnvironmentVariable)
        );

    /// <summary>The variable that names the appsettings overlay (<c>dev</c>, <c>prod</c>).</summary>
    public const string EnvironmentVariable = "HEROES_REPLAY_ENV";

    /// <summary>
    /// Where the commands read <c>appsettings.json</c>: the current directory when it has one,
    /// otherwise this exe's folder.
    /// </summary>
    public static string DefaultBasePath()
    {
        string basePath = Directory.GetCurrentDirectory();
        return File.Exists(Path.Combine(basePath, "appsettings.json"))
            ? basePath
            : AppContext.BaseDirectory;
    }

    /// <summary>
    /// The effective <c>OBS</c> section and <c>Location:DataDirectory</c> of the install in
    /// <paramref name="installDirectory"/>, through <see cref="BuildConfiguration"/>, so the
    /// <paramref name="environment"/> overlay and <c>HEROES_REPLAY_</c> variables apply as they do
    /// for that install's roles. No secret is resolved.
    /// </summary>
    public static (OBSSettings Obs, string DataDirectory) LoadInstallObsSettings(
        string installDirectory,
        string environment
    )
    {
        IConfigurationRoot configuration = BuildConfiguration(
            Path.GetFullPath(installDirectory),
            environment
        );
        return (
            configuration.GetSection("OBS").Get<OBSSettings>() ?? new OBSSettings(),
            configuration.GetSection("Location").Get<LocationSettings>()?.DataDirectory
        );
    }

    /// <summary>
    /// The effective settings of the install in <paramref name="basePath"/>: appsettings.json,
    /// secrets, the <paramref name="environment"/> overlay, then <c>HEROES_REPLAY_</c> variables.
    /// The release update reads the install it replaces through this.
    /// </summary>
    public static IConfigurationRoot BuildConfiguration(string basePath, string environment)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.secrets.json", optional: true);

        if (!string.IsNullOrWhiteSpace(environment))
        {
            builder.AddJsonFile($"appsettings.{environment}.json", optional: true);
        }

        builder.AddEnvironmentVariables("HEROES_REPLAY_");

        return builder.Build();
    }
}
