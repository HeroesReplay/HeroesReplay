using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using HeroesReplay.Core;
using HeroesReplay.Core.Processes;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using TwitchLib.Api;
using TwitchLib.Api.Core;
using TwitchLib.Api.Core.Interfaces;
using TwitchLib.Client;

using Windows.Media.Ocr;

namespace HeroesReplay.CLI
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddCoreServices(this IServiceCollection services, CancellationToken token, Type stormReplayProvider)
        {
            var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json")
                    .AddJsonFile("appsettings.secret.json")
                    .AddEnvironmentVariables("HEROES_REPLAY_")
                    .Build();

            CaptureMethod captureMethod = configuration.GetValue<CaptureMethod>("Settings:CaptureMethod");

            Type captureStrategy = captureMethod switch
            {
                CaptureMethod.None => typeof(StubCapture),
                CaptureMethod.BitBlt => typeof(CaptureBitBlt),
                CaptureMethod.CopyFromScreen => typeof(CaptureFromScreen),
                _ => typeof(CaptureBitBlt)
            };

            var weightingsType = typeof(IGameWeightings);
            var weightings = weightingsType.Assembly.GetTypes().Where(type => type.IsClass && weightingsType.IsAssignableFrom(type));

            foreach (var type in weightings)
            {
                services.AddSingleton(typeof(IGameWeightings), type);
            }

            return services
                .AddLogging(builder => builder.AddConfiguration(configuration.GetSection("Logging")).AddConsole())
                .AddSingleton<IConfiguration>(configuration)
                .Configure<Settings>(configuration.GetSection("Settings"))
                .AddSingleton(provider => new CancellationTokenProvider(token))
                .AddSingleton(provider => OcrEngine.TryCreateFromUserProfileLanguages())
                .AddSingleton(typeof(CaptureStrategy), captureStrategy)
                .AddSingleton(typeof(IGameController), captureMethod switch { CaptureMethod.None => typeof(StubController), _ => typeof(GameController) })
                .AddSingleton<GameDataService>()
                .AddSingleton<ReplayHelper>()                
                .AddSingleton<SessionHolder>()
                .AddSingleton<IGameManager, GameManager>()
                .AddSingleton<IReplayAnalzer, ReplayAnalyzer>()
                .AddSingleton<IGameSession, GameSession>()
                .AddSingleton(typeof(ISessionHolder), provider => provider.GetRequiredService<SessionHolder>())
                .AddSingleton(typeof(ISessionWriter), provider => provider.GetRequiredService<SessionHolder>())
                .AddSingleton<ISessionCreator, SessionCreator>()                
                .AddSingleton<TwitchClient>()
                .AddSingleton<TwitchAPI>()
                .AddSingleton<IApiSettings>(serviceProvider =>
                {
                    IOptions<Settings> options = serviceProvider.GetRequiredService<IOptions<Settings>>();
                    return new ApiSettings { AccessToken = options.Value.TwitchAccessToken, ClientId = options.Value.TwitchClientId };
                })
                .AddSingleton<HeroesProfileService>()
                .AddSingleton<HeroesProfileMatchDetailsWriter>()              
                .AddSingleton(typeof(IReplayProvider), stormReplayProvider)                
                .AddSingleton<SaltySadism>();
        }
    }
}