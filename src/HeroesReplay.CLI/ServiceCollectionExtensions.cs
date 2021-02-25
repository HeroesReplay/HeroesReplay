using System;
using System.IO;
using System.Linq;
using System.Threading;

using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Processes;
using HeroesReplay.Core.Providers;
using HeroesReplay.Core.Reports;
using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Obs;
using HeroesReplay.Core.Services.Twitch;
using HeroesReplay.Core.Services.Twitch.RewardHandlers;
using HeroesReplay.Core.Shared;

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

namespace HeroesReplay.CLI
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddYouTubeServices(this IServiceCollection services, CancellationToken token)
        {
            return services;
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
                .AddLogging(builder => builder.AddConfiguration(configuration.GetSection("Logging")).AddConsole().AddEventLog(config => config.SourceName = "Heroes Replay"))
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton(settings)
                .AddSingleton(new ConsoleTokenProvider(token))
                .AddSingleton<IGameData, GameData>()
                .AddSingleton<IReplayHelper, ReplayHelper>()
                .AddSingleton<IAbilityDetector, AbilityDetector>()
                .AddSingleton<IReplayAnalyzer, ReplayAnalyzer>()
                .AddSingleton<IReplayDetailsWriter, ReplayDetailsWriter>()
                .AddSingleton(typeof(IReplayProvider), replayProvider)
                .AddSingleton<ISpectateReportWriter, SpectateReportCsvWriter>();
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
                .AddLogging(builder => builder.AddConfiguration(configuration.GetSection("Logging")).AddConsole().AddEventLog(config => config.SourceName = "Heroes Replay"))
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton(implementationFactory: serviceProvider => serviceProvider.GetRequiredService<IConfiguration>().Get<AppSettings>())
                .AddSingleton(new ConsoleTokenProvider(token))
                .AddSingleton(OcrEngine.TryCreateFromUserProfileLanguages())
                .AddSingleton(typeof(CaptureStrategy), configuration.Get<AppSettings>().Capture.Method switch { CaptureMethod.None => typeof(StubCapture), _ => typeof(BitBltCapture) })
                .AddSingleton(typeof(IGameController), configuration.Get<AppSettings>().Capture.Method switch { CaptureMethod.None => typeof(StubController), _ => typeof(GameController) })
                .AddSingleton(typeof(ITalentNotifier), configuration.Get<AppSettings>().Capture.Method switch { CaptureMethod.None => typeof(StubNotifier), _ => typeof(TalentNotifier) })
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
                .AddSingleton<IReplayDetailsWriter, ReplayDetailsWriter>()
                .AddSingleton<IOnMessageHandler, OnMessageReceivedHandler>()
                .AddSingleton<IOnRewardHandler, OnRewardRedeemedHandler>()
                .AddSingleton<ISupportedRewardsHolder, SupportedRewardsHolder>()
                .AddSingleton<IRewardRequestFactory, RewardRequestFactory>()
                .AddSingleton<IRequestQueue, ReplayRequestQueue>()
                .AddSingleton<ITwitchExtensionService, TwitchExtensionService>()
                .AddSingleton<ITwitchBot, TwitchBot>()
                .AddSingleton<ITwitchClient, TwitchClient>()
                .AddSingleton<ITwitchPubSub, TwitchPubSub>()
                .AddSingleton<ITwitchAPI, TwitchAPI>()
                .AddSingleton(implementationFactory: serviceProvider =>
                {
                    AppSettings settings = serviceProvider.GetRequiredService<AppSettings>();
                    return new ConnectionCredentials(twitchUsername: settings.Twitch.Account, twitchOAuth: settings.Twitch.AccessToken);
                })
                .AddSingleton<IApiSettings>(implementationFactory: serviceProvider =>
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

            var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json")
                    .AddJsonFile($"appsettings.{env}.json", optional: false)
                    .AddJsonFile("appsettings.secrets.json", optional: false)
                    .AddEnvironmentVariables("HEROES_REPLAY_")
                    .Build();

            return configuration;
        }
    }
}