using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using HeroesReplay.CLI.Commands.Update;
using HeroesReplay.Core;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Analysis.Calculators;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Replays.Context;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.SelfUpdate;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating;
using HeroesReplay.Core.Spectating.Capture;
using HeroesReplay.Core.Spectating.Clock;
using HeroesReplay.Core.Spectating.Clock.Hybrid;
using HeroesReplay.Core.Spectating.Clock.Memory;
using HeroesReplay.Core.Spectating.Clock.Ocr;
using HeroesReplay.Core.Spectating.Control;
using HeroesReplay.Core.Spectating.Reports;
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
using Windows.Media.Ocr;

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
                    .AddEventLog(config => config.SourceName = "HeroesReplay.YouTubeService")
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
                    .AddEventLog(config => config.SourceName = "HeroesReplay.ReportService")
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

        return services
            .AddHeroesReplayOpenTelemetry(configuration, "heroesreplay-check")
            .AddMemoryCache()
            .AddLogging(builder =>
                builder.AddConfiguration(configuration.GetSection("Logging")).AddConsole()
            )
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(settings)
            .AddSingleton(OcrEngine.TryCreateFromUserProfileLanguages())
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
    /// The effective <c>OBS</c> section without resolving secrets, for commands that only need
    /// names and flags.
    /// </summary>
    public static OBSSettings LoadObsSettings() =>
        GetConfiguration().GetSection("OBS").Get<OBSSettings>() ?? new OBSSettings();

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
        );
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
            .AddSingleton<StormClientConfigurator>();
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
                    .AddEventLog(config => config.SourceName = "HeroesReplay.TwitchService")
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
            .AddSingleton<IRedemptionCanceller, RedemptionCanceller>()
            .AddSingleton<PredictionReportWriter>()
            .AddSingleton<IMatchPredictionService, TwitchMatchPredictionService>()
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
            .AddHeroesReplayOpenTelemetry(configuration, "heroesreplay-spectate")
            .AddMemoryCache()
            .AddLogging(builder =>
                builder
                    .AddConfiguration(configuration.GetSection("Logging"))
                    .AddConsole()
                    .AddEventLog(config => config.SourceName = "HeroesReplay.SpectatorService")
            )
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(settings)
            .AddSingleton(new CancellationTokenProvider(token))
            .AddSingleton(OcrEngine.TryCreateFromUserProfileLanguages())
            .AddSingleton(
                typeof(IGameCapture),
                settings.Capture.Method switch
                {
                    CaptureMethod.None => typeof(StubCapture),
                    CaptureMethod.BitBlt => typeof(BitBltCapture),
                    _ => typeof(PrintWindowCapture),
                }
            )
            .AddSingleton<MatchTimerFilter>()
            .AddSingleton<StableGameTimer>()
            .AddSingleton<OcrGameTimer>()
            .AddSingleton<IGameTimer, FallbackGameTimer>()
            .AddSingleton<GameTimerLog>()
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
            .AddSingleton(
                typeof(ITwitchBot),
                settings.Capture.Method switch
                {
                    CaptureMethod.None => typeof(FakeTwitchBot),
                    _ => typeof(TwitchBot),
                }
            )
            .AddSingleton(typeof(IReplayProvider), replayProvider)
            .AddSingleton<IGameData, GameData>()
            .AddSingleton<IReplayHelper, ReplayHelper>()
            .AddSingleton<IAbilityDetector, AbilityDetector>()
            .AddSingleton<IYouTubeReplayLookup, YouTubeReplayLookup>()
            .AddSingleton<RecordingClock>()
            .AddSingleton<IGameFirewall, NetshGameFirewall>()
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
            .AddSingleton<IOnMessageHandler, OnMessageReceivedHandler>()
            .AddSingleton<IOnRewardHandler, OnRewardRedeemedHandler>()
            .AddSingleton<ICustomRewardsHolder, SupportedRewardsHolder>()
            .AddSingleton<IRewardRequestFactory, RewardRequestFactory>()
            .AddSingleton<IRequestQueue, RequestQueue>()
            .AddSingleton(
                typeof(ITwitchClient),
                settings.Capture.Method switch
                {
                    CaptureMethod.None => typeof(FakeTwitchClient),
                    _ => typeof(TwitchClient),
                }
            )
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
            .AddSingleton<OBSWebsocket>()
            .AddSingleton<IObsController, ObsController>()
            .AddSingleton<IReleaseUpdateGate, ReleaseUpdateGate>()
            .AddSingleton<IEngine, Engine>()
            .AddSingleton<SpectatorStatusStore>()
            .AddConnectivityServices()
            .AddFocusCalculators();
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

    private static IConfigurationRoot GetConfiguration()
    {
        string basePath = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(basePath, "appsettings.json")))
        {
            basePath = AppContext.BaseDirectory;
        }

        return BuildConfiguration(
            basePath,
            Environment.GetEnvironmentVariable("HEROES_REPLAY_ENV")
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
