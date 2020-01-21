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

            Handler = CommandHandler.Create<FileInfo, bool, DirectoryInfo, CancellationToken>(CommandAsync);
        }

        private async Task CommandAsync(FileInfo path, bool launch, DirectoryInfo bnet, CancellationToken cancellationToken)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddEnvironmentVariables()
                .AddJsonFile("appsettings.json")
                .AddInMemoryCollection(new[] { new KeyValuePair<string, string>("bnet", bnet.FullName), new KeyValuePair<string, string>("path", path.FullName), new KeyValuePair<string, string>("launch", launch.ToString()), })
                .Build();

            ServiceProvider serviceProvider = new ServiceCollection()
                .AddLogging(loggingBuilder => loggingBuilder.AddConsole())
                .AddSingleton<IConfiguration>(provider => configuration)
                .AddSingleton((provider) => new CancellationTokenProvider(cancellationToken))
                .AddSingleton<HeroesOfTheStorm>()
                .AddSingleton<BattleNet>()
                .AddSingleton<StormReplayAnalyzer>()
                .AddSingleton<StormReplayHeroSelector>()
                .AddSingleton<StormReplaySpectator>()
                .AddSingleton<IStormReplayProvider, StormReplayFileProvider>()
                .AddSingleton<StormReplayConsumer>()
                .AddSingleton<StormReplayRunner>()
                .AddSingleton<AdminChecker>()
                .BuildServiceProvider();

            using (var scope = serviceProvider.CreateScope())
            {
                StormReplayConsumer stormReplayConsumer = scope.ServiceProvider.GetRequiredService<StormReplayConsumer>();

                await stormReplayConsumer.ReplayAsync(launch);
            }
        }
    }
}