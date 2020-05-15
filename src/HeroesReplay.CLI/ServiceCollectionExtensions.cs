using System;
using System.Threading;
using HeroesReplay.Core.Analyzer;
using HeroesReplay.Core.Processes;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddCoreServices(this IServiceCollection services, IConfigurationRoot configuration, CancellationToken token, CaptureMethod captureMethod, Type stormReplayProvider)
        {
            Type captureStrategy = captureMethod switch
            {
                CaptureMethod.None => typeof(StubCapture),
                CaptureMethod.BitBlt => typeof(CaptureBitBlt),
                CaptureMethod.CopyFromScreen => typeof(CaptureFromScreen),
                _ => typeof(CaptureBitBlt)
            };

            return services
                .AddLogging(builder => builder.AddConfiguration(configuration.GetSection("Logging")).AddConsole())
                .AddSingleton<IConfiguration>(configuration)
                .Configure<Settings>(configuration.GetSection("Settings"))
                .AddSingleton(provider => new CancellationTokenProvider(token))
                .AddSingleton(typeof(HeroesOfTheStorm), captureMethod switch { CaptureMethod.None => typeof(StubOfTheStorm), _ => typeof(HeroesOfTheStorm) })
                .AddSingleton(typeof(CaptureStrategy), captureStrategy)
                .AddSingleton<GameDataService>()
                .AddSingleton<ReplayHelper>()
                .AddSingleton<StormReplayAnalyzer>()
                .AddSingleton<HeroesProfileService>()
                .AddSingleton<ReplayDetailsWriter>()
                .AddSingleton<StormPlayerTool>()
                .AddSingleton<GamePanelTool>()
                .AddSingleton<GameStateTool>()
                .AddSingleton<DebugTool>()
                .AddSingleton<SpectateTool>()
                .AddSingleton<StormReplaySpectator>()
                .AddSingleton<ReplayConsumer>()
                .AddSingleton<ReplayRunner>()
                .AddSingleton(typeof(IReplayProvider), stormReplayProvider);
        }
    }
}