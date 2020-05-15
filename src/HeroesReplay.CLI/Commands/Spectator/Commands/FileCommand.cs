using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.Options;
using HeroesReplay.Core.Processes;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands
{
    public class FileCommand : Command
    {
        public FileCommand() : base("file", "The individual .StormReplay file to spectate.")
        {
            AddOption(new StormReplayFileOption());
            AddOption(new LaunchOption());
            AddOption(new CaptureMethodOption());

            Handler = CommandHandler.Create<FileInfo, bool, CaptureMethod, CancellationToken>(CommandAsync);
        }

        protected async Task CommandAsync(FileInfo path, bool launch, CaptureMethod captureMethod, CancellationToken cancellationToken)
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

            var serviceProvider = new ServiceCollection()
                .AddCoreServices(configuration, cancellationToken, captureMethod, typeof(FileProvider))
                .BuildServiceProvider();

            using (IServiceScope scope = serviceProvider.CreateScope())
            {
                ReplayConsumer stormReplayConsumer = scope.ServiceProvider.GetRequiredService<ReplayConsumer>();

                await stormReplayConsumer.RunAsync(launch);
            }
        }
    }
}