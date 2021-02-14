using System;
using System.IO;
using System.Linq;
using System.Threading;

using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Processes;
using HeroesReplay.Core.Providers;
using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Obs;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using OBSWebsocketDotNet;

using TwitchLib.Api;
using TwitchLib.Api.Core;
using TwitchLib.Api.Core.Interfaces;
using TwitchLib.Client;

using Windows.Media.Ocr;

namespace HeroesReplay.CLI
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddCoreServices(this IServiceCollection services, CancellationToken token, Type replayProvider)
        {
            var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json")
                    .AddEnvironmentVariables("HEROES_REPLAY_")
                    .Build();

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
                .AddSingleton(new CancellationTokenProvider(token))
                .AddSingleton(OcrEngine.TryCreateFromUserProfileLanguages())
                .AddSingleton(typeof(CaptureStrategy), settings.Capture.Method switch { CaptureMethod.None => typeof(CaptureStub), CaptureMethod.BitBlt => typeof(CaptureBitBlt), _ => typeof(CaptureBitBlt) })
                .AddSingleton(typeof(IGameController), settings.Capture.Method switch { CaptureMethod.None => typeof(StubController), _ => typeof(GameController) })
                .AddSingleton<IGameData, GameData>()
                .AddSingleton<IReplayHelper, ReplayHelper>()
                .AddSingleton<SessionHolder>()
                .AddSingleton<OBSWebsocket>()
                .AddSingleton<IAbilityDetector, AbilityDetector>()
                .AddSingleton<IGameManager, GameManager>()
                .AddSingleton<IReplayAnalzer, ReplayAnalyzer>()
                .AddSingleton<IObsController, ObsController>()
                .AddSingleton<IGameSession, GameSession>()
                .AddSingleton(typeof(ISessionHolder), provider => provider.GetRequiredService<SessionHolder>())
                .AddSingleton(typeof(ISessionSetter), provider => provider.GetRequiredService<SessionHolder>())
                .AddSingleton<ISessionCreator, SessionCreator>()
                .AddSingleton<TwitchClient>()
                .AddSingleton<TwitchAPI>()
                .AddSingleton<IApiSettings>(implementationFactory: serviceProvider =>
                {
                    AppSettings settings = serviceProvider.GetRequiredService<AppSettings>();
                    return new ApiSettings { AccessToken = settings.TwitchApi.AccessToken, ClientId = settings.TwitchApi.ClientId };
                })
                .AddSingleton<IHeroesProfileService, HeroesProfileService>()
                .AddSingleton<IHeroesProfileExtensionPayloadsBuilder, HeroesProfileExtensionPayloadsBuilder>()
                .AddSingleton<IReplayDetailsWriter, ReplayDetailsWriter>()
                .AddSingleton<ITalentNotifier, TalentNotifier>()
                .AddSingleton(typeof(IReplayProvider), replayProvider)
                .AddSingleton<SaltySadism>();
        }
    }
}