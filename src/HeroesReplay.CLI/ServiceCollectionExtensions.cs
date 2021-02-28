using System;
using System.IO;
using System.Linq;
using System.Threading;

using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Twitch;
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
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Analysis;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.HeroesProfileExtension;
using HeroesReplay.Core.Services.Observer;
using HeroesReplay.Core.Services.Providers;
using HeroesReplay.Core.Services.Queue;
using HeroesReplay.Core.Services.Reports;
using HeroesReplay.Core.Services.Shared;
using HeroesReplay.Core.Services.Twitch.ChatMessages;
using HeroesReplay.Core.Services.Twitch.RedeemedRewards;
using HeroesReplay.Core.Services.Twitch.Rewards;
using HeroesReplay.Core.Services.Context;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using HeroesReplay.Core.Services.YouTube;

namespace HeroesReplay.CLI
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddYouTubeServices(this IServiceCollection services, CancellationToken token)
        {
            IConfigurationRoot configuration = GetConfiguration();

            return services
                .AddMemoryCache()
                .AddSingleton<IAsyncCacheProvider, MemoryCacheProvider>()
                .AddLogging(builder => builder.AddConfiguration(configuration.GetSection("Logging")).AddConsole().AddEventLog(config => config.SourceName = "HeroesReplay.YouTubeService"))
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton<IYouTubeUploader, YouTubeUploader>()
                .AddSingleton(serviceProvider => serviceProvider.GetRequiredService<IConfiguration>().Get<AppSettings>())
                .AddSingleton(CancellationTokenSource.CreateLinkedTokenSource(token))
                .AddSingleton<IConfiguration>(configuration);
        }

        public static IServiceCollection AddReportServices(this IServiceCollection services, CancellationToken token, Type replayProvider)
        {
            IConfigurationRoot configuration = GetConfiguration();

            var focusCalculator = typeof(IFocusCalculator);
            var calculators = focusCalculator.Assembly.GetTypes().Where(type => type.IsClass && focusCalculator.IsAssignableFrom(type));

            foreach (var calculatorType in calculators)
            {
                services.AddSingleton(focusCalculator, calculatorType);
            }

            var settings = configuration.Get<AppSettings>();

            return services
                .AddLogging(builder => builder.AddConfiguration(configuration.GetSection("Logging")).AddConsole().AddEventLog(config => config.SourceName = "HeroesReplay.ReportService"))
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton(settings)
                .AddSingleton(new CancellationTokenProvider(token))
                .AddSingleton<IGameData, GameData>()
                .AddSingleton<IReplayHelper, ReplayHelper>()
                .AddSingleton<IAbilityDetector, AbilityDetector>()
                .AddSingleton<IReplayAnalyzer, ReplayAnalyzer>()
                .AddSingleton<IContextFileManager, ContextFileManager>()
                .AddSingleton(typeof(IReplayProvider), replayProvider)
                .AddSingleton<ISpectateReportWriter, SpectateReportCsvWriter>();
        }

        public static IServiceCollection AddTwitchServices(this IServiceCollection services, CancellationToken token)
        {
            IConfigurationRoot configuration = GetConfiguration();

            var rewardHandler = typeof(IRewardHandler);
            var rewardHandlerTypes = rewardHandler.Assembly.GetTypes().Where(type => type.IsClass && rewardHandler.IsAssignableFrom(type));

            foreach (var type in rewardHandlerTypes)
            {
                services.AddSingleton(rewardHandler, type);
            }

            var commandHandler = typeof(IMessageHandler);
            var commandHandlerTypes = rewardHandler.Assembly.GetTypes().Where(type => type.IsClass && commandHandler.IsAssignableFrom(type));

            foreach (var type in commandHandlerTypes)
            {
                services.AddSingleton(commandHandler, type);
            }

            return services
                .AddMemoryCache()
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton(serviceProvider => serviceProvider.GetRequiredService<IConfiguration>().Get<AppSettings>())
                .AddSingleton(new CancellationTokenProvider(token))
                .AddSingleton<IAsyncCacheProvider, MemoryCacheProvider>()
                .AddLogging(builder => builder.AddConfiguration(configuration.GetSection("Logging")).AddConsole().AddEventLog(config => config.SourceName = "HeroesReplay.TwitchService"))
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton(serviceProvider => serviceProvider.GetRequiredService<IConfiguration>().Get<AppSettings>())
                .AddSingleton(typeof(ITwitchBot), configuration.Get<AppSettings>().Capture.Method switch { CaptureMethod.None => typeof(FakeTwitchBot), _ => typeof(TwitchBot) })
                .AddSingleton(typeof(ITwitchClient), configuration.Get<AppSettings>().Capture.Method switch { CaptureMethod.None => typeof(FakeTwitchClient), _ => typeof(TwitchClient) })
                .AddSingleton(new CancellationTokenProvider(token))
                .AddSingleton<ITwitchRewardsManager, TwitchRewardsManager>()
                .AddSingleton<IGameData, GameData>()
                .AddSingleton<ITwitchAPI, TwitchAPI>()
                .AddHttpClient<HeroesProfileService>().Services
                .AddSingleton<IHeroesProfileService, HeroesProfileService>()
                .AddSingleton<ITwitchPubSub, TwitchPubSub>()
                .AddSingleton<ITwitchAPI, TwitchAPI>()
                .AddSingleton(serviceProvider =>
                {
                    AppSettings settings = serviceProvider.GetRequiredService<AppSettings>();
                    return new ConnectionCredentials(settings.Twitch.Account, settings.Twitch.AccessToken);
                })
                .AddSingleton<IApiSettings>(serviceProvider =>
                {
                    AppSettings settings = serviceProvider.GetRequiredService<AppSettings>();
                    return new ApiSettings { AccessToken = settings.Twitch.AccessToken, ClientId = settings.Twitch.ClientId };
                })
                .AddSingleton<ICustomRewardsHolder, SupportedRewardsHolder>();
        }

        public static IServiceCollection AddSpectateServices(this IServiceCollection services, CancellationToken token, Type replayProvider)
        {
            IConfigurationRoot configuration = GetConfiguration();

            var focusCalculator = typeof(IFocusCalculator);
            var calculatorTypes = focusCalculator.Assembly.GetTypes().Where(type => type.IsClass && focusCalculator.IsAssignableFrom(type));

            foreach (var type in calculatorTypes)
            {
                services.AddSingleton(focusCalculator, type);
            }

            var rewardHandler = typeof(IRewardHandler);
            var rewardHandlerTypes = rewardHandler.Assembly.GetTypes().Where(type => type.IsClass && rewardHandler.IsAssignableFrom(type));

            foreach (var type in rewardHandlerTypes)
            {
                services.AddSingleton(rewardHandler, type);
            }

            var commandHandler = typeof(IMessageHandler);
            var commandHandlerTypes = rewardHandler.Assembly.GetTypes().Where(type => type.IsClass && commandHandler.IsAssignableFrom(type));

            foreach (var type in commandHandlerTypes)
            {
                services.AddSingleton(commandHandler, type);
            }

            return services
                .AddMemoryCache()
                .AddSingleton<IAsyncCacheProvider, MemoryCacheProvider>()
                .AddLogging(builder => builder.AddConfiguration(configuration.GetSection("Logging")).AddConsole().AddEventLog(config => config.SourceName = "HeroesReplay.SpectatorService"))
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton(serviceProvider => serviceProvider.GetRequiredService<IConfiguration>().Get<AppSettings>())
                .AddSingleton(new CancellationTokenProvider(token))
                .AddSingleton(OcrEngine.TryCreateFromUserProfileLanguages())
                .AddSingleton(typeof(CaptureStrategy), configuration.Get<AppSettings>().Capture.Method switch { CaptureMethod.None => typeof(StubCapture), _ => typeof(BitBltCapture) })
                .AddSingleton(typeof(IGameController), configuration.Get<AppSettings>().Capture.Method switch { CaptureMethod.None => typeof(StubController), _ => typeof(GameController) })
                .AddSingleton(typeof(ITalentNotifier), configuration.Get<AppSettings>().Capture.Method switch { CaptureMethod.None => typeof(StubNotifier), _ => typeof(TalentNotifier) })
                .AddSingleton(typeof(ITwitchBot), configuration.Get<AppSettings>().Capture.Method switch { CaptureMethod.None => typeof(FakeTwitchBot), _ => typeof(TwitchBot) })
                .AddSingleton(typeof(IReplayProvider), replayProvider)
                .AddSingleton<IGameData, GameData>()
                .AddSingleton<IReplayHelper, ReplayHelper>()
                .AddSingleton<IAbilityDetector, AbilityDetector>()
                .AddSingleton<IGameManager, GameManager>()
                .AddSingleton<IReplayAnalyzer, ReplayAnalyzer>()
                .AddSingleton<ISpectator, Spectator>()
                .AddSingleton<IReplayLoader, ReplayLoader>()
                .AddSingleton<ReplayContext>()
                .AddSingleton<IReplayContextSetter>(provider => provider.GetRequiredService<ReplayContext>())
                .AddSingleton<IReplayContext>(provider => provider.GetRequiredService<ReplayContext>())
                .AddHttpClient<TwitchExtensionService>().Services
                .AddHttpClient<HeroesProfileService>().Services
                .AddSingleton<IHeroesProfileService, HeroesProfileService>()
                .AddSingleton<IExtensionPayloadsBuilder, ExtensionPayloadBuilder>()
                .AddSingleton<IContextFileManager, ContextFileManager>()
                .AddSingleton<IOnMessageHandler, OnMessageReceivedHandler>()
                .AddSingleton<IOnRewardHandler, OnRewardRedeemedHandler>()
                .AddSingleton<ICustomRewardsHolder, SupportedRewardsHolder>()
                .AddSingleton<IRewardRequestFactory, RewardRequestFactory>()
                .AddSingleton<IRequestQueue, RequestQueue>()
                .AddSingleton<ITwitchExtensionService, TwitchExtensionService>()
                .AddSingleton(typeof(ITwitchClient), configuration.Get<AppSettings>().Capture.Method switch { CaptureMethod.None => typeof(FakeTwitchClient), _ => typeof(TwitchClient) })
                .AddSingleton<ITwitchPubSub, TwitchPubSub>()
                .AddSingleton<ITwitchAPI, TwitchAPI>()
                .AddSingleton(serviceProvider =>
                {
                    AppSettings settings = serviceProvider.GetRequiredService<AppSettings>();
                    return new ConnectionCredentials(settings.Twitch.Account, settings.Twitch.AccessToken);
                })
                .AddSingleton<IApiSettings>(serviceProvider =>
                {
                    AppSettings settings = serviceProvider.GetRequiredService<AppSettings>();
                    return new ApiSettings { AccessToken = settings.Twitch.AccessToken, ClientId = settings.Twitch.ClientId };
                })
                .AddSingleton<OBSWebsocket>()
                .AddSingleton<IObsController, ObsController>()
                .AddSingleton<IEngine, Engine>();
        }

        private static IConfigurationRoot GetConfiguration()
        {
            var env = Environment.GetEnvironmentVariable("HEROES_REPLAY_ENV");

            var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json")
                    .AddJsonFile($"appsettings.{env}.json", false)
                    .AddJsonFile("appsettings.secrets.json", false)
                    .AddEnvironmentVariables("HEROES_REPLAY_");

            return builder.Build();
        }
    }
}