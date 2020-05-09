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
    public class SpectateHotsApiS3Command : Command
    {
        public SpectateHotsApiS3Command() : base("hotsapi", "Access the HotsApi database to download uploaded replays and spectate them.")
        {
            AddOption(new LaunchOption());
            AddOption(new MinimumReplayIdOption(Constants.REPLAY_ID_UNSET));
            AddOption(new AwsAccessKeyOption(Constants.HEROES_REPLAY_AWS_ACCESS_KEY));
            AddOption(new AwsSecretKeyOption(Constants.HEROES_REPLAY_AWS_SECRET_KEY));
            AddOption(new CaptureMethodOption());
            AddOption(new HeroesProfileApiKey(Constants.HEROES_PROFILE_API_KEY));

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

            ServiceProvider serviceProvider = new ServiceCollection()
                .AddLogging(builder => builder.AddConfiguration(configuration.GetSection("Logging")).AddConsole())
                .AddSingleton<IConfiguration>(provider => configuration)
                .AddSingleton(provider => new CancellationTokenProvider(cancellationToken))
                .AddSingleton(typeof(HeroesOfTheStorm), captureMethod switch { CaptureMethod.None => typeof(StubOfTheStorm), _ => typeof(HeroesOfTheStorm) })
                .AddSingleton(typeof(CaptureStrategy), captureMethod switch { CaptureMethod.None => typeof(StubCapture), CaptureMethod.BitBlt => typeof(CaptureBitBlt), CaptureMethod.CopyFromScreen => typeof(CaptureFromScreen), _ => typeof(CaptureBitBlt) })
                .AddSingleton<StormReplayAnalyzer>()
                .AddSingleton<StormPlayerTool>()
                .AddSingleton<GamePanelTool>()
                .AddSingleton<GameStateTool>()
                .AddSingleton<DebugTool>()
                .AddSingleton<SpectateTool>()
                .AddSingleton<StormReplaySpectator>()
                .AddSingleton<StormReplayDetailsWriter>()
                .AddSingleton<PlayerBlackListChecker>()
                .AddSingleton<IStormReplayProvider, StormReplayHotsApiProvider>()
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