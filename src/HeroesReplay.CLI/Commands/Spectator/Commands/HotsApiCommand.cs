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
    public class HotsApiCommand : Command
    {
        public HotsApiCommand() : base("hotsapi", "Access the HotsApi database to download uploaded replays and spectate them.")
        {
            AddOption(new LaunchOption());
            AddOption(new MinimumReplayIdOption(-1));
            AddOption(new AwsAccessKeyOption(Constants.EnvironmentVariables.HEROES_REPLAY_AWS_ACCESS_KEY));
            AddOption(new AwsSecretKeyOption(Constants.EnvironmentVariables.HEROES_REPLAY_AWS_SECRET_KEY));
            AddOption(new HeroesProfileApiKey(Constants.EnvironmentVariables.HEROES_PROFILE_API_KEY));
            AddOption(new CaptureMethodOption());

            Handler = CommandHandler.Create<int, string, string, string, bool, CaptureMethod, CancellationToken>(CommandAsync);
        }

        protected virtual async Task CommandAsync(int minReplayId, string awsAccessKey, string awsSecretKey, string heroesProfileApiKey, bool launch, CaptureMethod captureMethod, CancellationToken cancellationToken)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string>(Constants.ConfigKeys.MinReplayId, minReplayId.ToString()),
                    new KeyValuePair<string, string>(Constants.ConfigKeys.Launch, launch.ToString()),
                    new KeyValuePair<string, string>(Constants.ConfigKeys.AwsAccessKey, awsAccessKey),
                    new KeyValuePair<string, string>(Constants.ConfigKeys.AwsSecretKey, awsSecretKey),
                    new KeyValuePair<string, string>(Constants.ConfigKeys.HeroesProfileApiKey, heroesProfileApiKey)
                })
                .Build();

            var serviceProvider = new ServiceCollection()
            .AddCoreServices(configuration, cancellationToken, captureMethod, typeof(HotsApiProvider))
            .BuildServiceProvider();

            using (IServiceScope scope = serviceProvider.CreateScope())
            {
                ReplayConsumer stormReplayConsumer = scope.ServiceProvider.GetRequiredService<ReplayConsumer>();

                await stormReplayConsumer.RunAsync(launch);
            }
        }
    }
}