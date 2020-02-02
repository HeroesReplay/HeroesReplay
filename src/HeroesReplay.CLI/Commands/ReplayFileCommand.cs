using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Analyzer;
using HeroesReplay.CLI.Options;
using HeroesReplay.Processes;
using HeroesReplay.Replays;
using HeroesReplay.Runner;
using HeroesReplay.Shared;
using HeroesReplay.Spectator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI.Commands
{
    public class ReplayFileCommand : Command
    {
        public ReplayFileCommand() : base("file", "The individual .StormReplay file to spectate.")
        {
            AddOption(new StormReplayFileOption());
            AddOption(new LaunchOption());
            AddOption(new BattlenetOption());
            AddOption(new CaptureMethodOption());

            Handler = CommandHandler.Create<FileInfo, bool, DirectoryInfo, CaptureMethod, CancellationToken>(CommandAsync);
        }

        private async Task CommandAsync(FileInfo path, bool launch, DirectoryInfo bnet, CaptureMethod captureMethod, CancellationToken cancellationToken)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddEnvironmentVariables()
                .AddJsonFile("appsettings.json")
                .AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string>(Constants.ConfigKeys.BattleNetPath, bnet.FullName),
                    new KeyValuePair<string, string>(Constants.ConfigKeys.ReplayProviderPath, path.FullName), 
                    new KeyValuePair<string, string>(Constants.ConfigKeys.Launch, launch.ToString()),
                })
                .Build();

            ServiceProvider serviceProvider = new ServiceCollection()
                .AddLogging(builder => builder.AddConfiguration(configuration.GetSection("Logging")).AddConsole())
                .AddSingleton<IConfiguration>(provider => configuration)
                .AddSingleton((provider) => new CancellationTokenProvider(cancellationToken))
                .AddSingleton(captureMethod == CaptureMethod.Stub ? typeof(StubOfTheStorm) : typeof(HeroesOfTheStorm))
                .AddSingleton<BattleNet>()
                .AddSingleton<ScreenCapture>((provider => new ScreenCapture(captureMethod)))
                .AddSingleton<StormReplayAnalyzer>()
                .AddSingleton<StormReplayDetailsWriter>()
                .AddSingleton<StormPlayerTool>()
                .AddSingleton<GamePanelTool>()
                .AddSingleton<GameStateTool>()
                .AddSingleton<SpectateTool>()
                .AddSingleton<StormReplaySpectator>()
                .AddSingleton<IStormReplayProvider, StormReplayFileProvider>()
                .AddSingleton<StormReplayConsumer>()
                .AddSingleton<StormReplayRunner>()
                .AddSingleton<AdminChecker>()
                .BuildServiceProvider();

            using (IServiceScope scope = serviceProvider.CreateScope())
            {
                StormReplayConsumer stormReplayConsumer = scope.ServiceProvider.GetRequiredService<StormReplayConsumer>();

                await stormReplayConsumer.ReplayAsync(launch);
            }
        }
    }
}