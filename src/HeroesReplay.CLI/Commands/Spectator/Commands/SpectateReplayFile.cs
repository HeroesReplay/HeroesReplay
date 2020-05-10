using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.Options;
using HeroesReplay.Core.Analyzer;
using HeroesReplay.Core.Processes;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands
{
    public class SpectateReplayFile : Command
    {
        public SpectateReplayFile() : base("file", "The individual .StormReplay file to spectate.")
        {
            AddOption(new StormReplayFileOption());
            AddOption(new LaunchOption());
            AddOption(new CaptureMethodOption());

            Handler = CommandHandler.Create<FileInfo, bool, CaptureMethod, CancellationToken>(CommandAsync);
        }

        protected async Task CommandAsync(FileInfo path, bool launch,CaptureMethod captureMethod, CancellationToken cancellationToken)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddEnvironmentVariables()
                .AddJsonFile("appsettings.json")
                .AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string>(Constants.ConfigKeys.ReplaySource, path.FullName), 
                    new KeyValuePair<string, string>(Constants.ConfigKeys.Launch, launch.ToString())
                })
                .Build();

            ServiceProvider serviceProvider = new ServiceCollection()
                .AddLogging(builder => builder.AddConfiguration(configuration.GetSection("Logging")).AddConsole())
                .AddSingleton<IConfiguration>(provider => configuration)
                .AddSingleton(provider => new CancellationTokenProvider(cancellationToken))
                .AddSingleton(typeof(HeroesOfTheStorm), captureMethod switch { CaptureMethod.None => typeof(StubOfTheStorm), _ => typeof(HeroesOfTheStorm) })
                .AddSingleton(typeof(CaptureStrategy), captureMethod switch { CaptureMethod.None => typeof(StubCapture), CaptureMethod.BitBlt => typeof(CaptureBitBlt), CaptureMethod.CopyFromScreen => typeof(CaptureFromScreen) })
                .AddSingleton<StormReplayAnalyzer>()
                .AddSingleton<StormReplayMMRCalculator>()
                .AddSingleton<StormReplayDetailsWriter>()
                .AddSingleton<StormPlayerTool>()
                .AddSingleton<GamePanelTool>()
                .AddSingleton<GameStateTool>()
                .AddSingleton<DebugTool>()
                .AddSingleton<SpectateTool>()
                .AddSingleton<StormReplaySpectator>()
                .AddSingleton<IStormReplayProvider, StormReplayFileProvider>()
                .AddSingleton<StormReplayConsumer>()
                .AddSingleton<StormReplayRunner>()
                .BuildServiceProvider();

            using (IServiceScope scope = serviceProvider.CreateScope())
            {
                StormReplayConsumer stormReplayConsumer = scope.ServiceProvider.GetRequiredService<StormReplayConsumer>();

                await stormReplayConsumer.RunAsync(launch);
            }
        }
    }
}