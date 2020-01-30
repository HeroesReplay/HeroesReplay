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
    public class ReplayDirectoryCommand : Command
    {
        public ReplayDirectoryCommand() : base("directory", "The directory that contains .StormReplay files to spectate.")
        {
            AddOption(new StormReplayDirectoryOption());
            AddOption(new LaunchOption());
            AddOption(new BattlenetOption());
            AddOption(new CaptureMethodOption());

            Handler = CommandHandler.Create<DirectoryInfo, bool, DirectoryInfo, CaptureMethod, CancellationToken>(ActionAsync);
        }

        private async Task ActionAsync(DirectoryInfo path, bool launch, DirectoryInfo bnet, CaptureMethod captureMethod, CancellationToken cancellationToken)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string>(Constants.ConfigKeys.BattleNetPath, bnet.FullName), 
                    new KeyValuePair<string, string>(Constants.ConfigKeys.ReplayProviderPath, path.FullName),
                    new KeyValuePair<string, string>(Constants.ConfigKeys.Launch, launch.ToString()),
                })
                .Build();


            ServiceProvider serviceProvider = new ServiceCollection()
                .AddLogging(loggingBuilder => loggingBuilder.AddConsole())
                .AddSingleton<IConfiguration>(provider => configuration)
                .AddSingleton((provider) => new CancellationTokenProvider(cancellationToken))
                .AddSingleton<HeroesOfTheStorm>()
                .AddSingleton<BattleNet>()
                .AddSingleton<ScreenCapture>((provider => new ScreenCapture(captureMethod)))
                .AddSingleton<StormReplayAnalyzer>()
                .AddSingleton<StormReplayHeroSelector>()
                .AddSingleton<StormReplayDetailsWriter>()
                .AddSingleton<StormReplaySpectator>()
                .AddSingleton<IStormReplayProvider, StormReplayDirectoryProvider>()
                .AddSingleton<StormReplayConsumer>()
                .AddSingleton<StormReplayRunner>()
                .BuildServiceProvider();

            using (var scope = serviceProvider.CreateScope())
            {
                StormReplayConsumer stormReplayConsumer = scope.ServiceProvider.GetRequiredService<StormReplayConsumer>();

                await stormReplayConsumer.ReplayAsync(launch);
            }
        }
    }
}