using System;
using System.IO;
using System.Linq;
using System.Threading;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Analysis;
using HeroesReplay.Core.Services.Analysis.Calculators;
using HeroesReplay.Core.Services.Client;
using HeroesReplay.Core.Services.Context;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.HeroesProfileExtension;
using HeroesReplay.Core.Services.Observer;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using HeroesReplay.Core.Services.Providers;
using HeroesReplay.Core.Services.Queue;
using HeroesReplay.Core.Services.Reports;
using HeroesReplay.Core.Services.Shared;
using HeroesReplay.Core.Services.Status;
using HeroesReplay.Core.Services.Twitch;
using HeroesReplay.Core.Services.Twitch.ChatMessages;
using HeroesReplay.Core.Services.Twitch.RedeemedRewards;
using HeroesReplay.Core.Services.Twitch.Rewards;
using HeroesReplay.Core.Services.YouTube;
using HeroesReplay.HeroesProfile.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OBSWebsocketDotNet;
using Polly.Caching;
using Polly.Caching.Memory;
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
            .AddHeroesReplayOpenTelemetry(configuration)
            .AddMemoryCache()
            .AddSingleton<IAsyncCacheProvider, MemoryCacheProvider>()
            .AddLogging(builder =>
                builder
                    .AddConfiguration(configuration.GetSection("Logging"))
                    .AddConsole()
                    .AddEventLog(config => config.SourceName = "HeroesReplay.YouTubeService")
            )
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<IYouTubeUploader, YouTubeUploader>()
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
            .AddHeroesReplayOpenTelemetry(configuration)
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
            .AddHeroesReplayOpenTelemetry(configuration)
            .AddMemoryCache()
            .AddSingleton<IAsyncCacheProvider, MemoryCacheProvider>()
            .AddLogging(builder =>
                builder.AddConfiguration(configuration.GetSection("Logging")).AddConsole()
            )
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(settings)
            .AddSingleton<StormClientConfigurator>()
            .AddSingleton(new CancellationTokenProvider(token))
            .AddHeroesProfileKiotaClient()
            .AddHttpClient<IHeroesProfileService, HeroesProfileService>()
            .Services.AddSingleton<OBSWebsocket>()
            .AddSingleton<ITwitchAPI, TwitchAPI>()
            .AddSingleton<IApiSettings>(serviceProvider =>
            {
                AppSettings bound = serviceProvider.GetRequiredService<AppSettings>();
                return new ApiSettings
                {
                    AccessToken = bound.Twitch?.AccessToken,
                    ClientId = bound.Twitch?.ClientId,
                };
            });
    }

    public static AppSettings BindSettings(IConfiguration configuration)
    {
        AppSettings settings =
            configuration.Get<AppSettings>()
            ?? throw new InvalidOperationException(
                "Could not bind AppSettings from configuration."
            );
        SecretResolver.Apply(settings);
        return settings;
    }

    public static IServiceCollection AddClientServices(this IServiceCollection services)
    {
        IConfigurationRoot configuration = GetConfiguration();
        AppSettings settings = BindSettings(configuration);
        return services
            .AddHeroesReplayOpenTelemetry(configuration)
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
            .AddSingleton<IFocusCalculator, DestroyingStructureCalculator>()
            .AddSingleton<IFocusCalculator, VehicleCalculator>()
            .AddSingleton<IFocusCalculator, EmotingCalculator>();
    }

    public static IServiceCollection AddTwitchServices(
        this IServiceCollection services,
        CancellationToken token
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
            .AddHeroesReplayOpenTelemetry(configuration)
            .AddMemoryCache()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(settings)
            .AddSingleton<IObserverPanelRequests, ObserverPanelRequests>()
            .AddSingleton(new CancellationTokenProvider(token))
            .AddSingleton<IAsyncCacheProvider, MemoryCacheProvider>()
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
            .AddSingleton<IMatchPredictionService, TwitchMatchPredictionService>()
            .AddSingleton<ITwitchRewardsManager, TwitchRewardsManager>()
            .AddSingleton<IGameData, GameData>()
            .AddSingleton<ITwitchAPI, TwitchAPI>()
            .AddHeroesProfileKiotaClient()
            .AddHttpClient<HeroesProfileService>()
            .Services.AddSingleton<IHeroesProfileService, HeroesProfileService>()
            .AddSingleton<ITwitchPubSub, TwitchPubSub>()
            .AddSingleton<ITwitchAPI, TwitchAPI>()
            .AddSingleton(serviceProvider =>
            {
                AppSettings settings = serviceProvider.GetRequiredService<AppSettings>();
                return new ConnectionCredentials(
                    settings.Twitch.Account,
                    settings.Twitch.AccessToken
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
            .AddSingleton<ICustomRewardsHolder, SupportedRewardsHolder>();
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
            .AddHeroesReplayOpenTelemetry(configuration)
            .AddMemoryCache()
            .AddSingleton<IAsyncCacheProvider, MemoryCacheProvider>()
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
                typeof(CaptureStrategy),
                settings.Capture.Method switch
                {
                    CaptureMethod.None => typeof(StubCapture),
                    _ => typeof(BitBltCapture),
                }
            )
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
            .AddHttpClient<TwitchExtensionService>()
            .Services.AddHeroesProfileKiotaClient()
            .AddHttpClient<HeroesProfileService>()
            .Services.AddSingleton<IHeroesProfileService, HeroesProfileService>()
            .AddSingleton<IExtensionPayloadsBuilder, ExtensionPayloadBuilder>()
            .AddSingleton<IContextFileManager, ContextFileManager>()
            .AddSingleton<IOnMessageHandler, OnMessageReceivedHandler>()
            .AddSingleton<IOnRewardHandler, OnRewardRedeemedHandler>()
            .AddSingleton<ICustomRewardsHolder, SupportedRewardsHolder>()
            .AddSingleton<IRewardRequestFactory, RewardRequestFactory>()
            .AddSingleton<IRequestQueue, RequestQueue>()
            .AddSingleton<ITwitchExtensionService, TwitchExtensionService>()
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
            .AddSingleton<IMatchPredictionService, TwitchMatchPredictionService>()
            .AddSingleton(serviceProvider =>
            {
                AppSettings settings = serviceProvider.GetRequiredService<AppSettings>();
                return new ConnectionCredentials(
                    settings.Twitch.Account,
                    settings.Twitch.AccessToken
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
            .AddSingleton<IEngine, Engine>()
            .AddSingleton<SpectatorStatusStore>()
            .AddFocusCalculators();
    }

    private static IServiceCollection AddHeroesProfileKiotaClient(this IServiceCollection services)
    {
        return services.AddSingleton(sp =>
        {
            HeroesProfileApiSettings api = sp.GetRequiredService<AppSettings>().HeroesProfileApi;
            return HeroesProfileClientFactory.Create(api.ApiKey, api.ExternalV1BaseUri);
        });
    }

    private static IConfigurationRoot GetConfiguration()
    {
        var env = Environment.GetEnvironmentVariable("HEROES_REPLAY_ENV");
        string basePath = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(basePath, "appsettings.json")))
        {
            basePath = AppContext.BaseDirectory;
        }

        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.secrets.json", optional: true);

        if (!string.IsNullOrWhiteSpace(env))
        {
            builder.AddJsonFile($"appsettings.{env}.json", optional: true);
        }

        builder.AddEnvironmentVariables("HEROES_REPLAY_");

        return builder.Build();
    }
}
