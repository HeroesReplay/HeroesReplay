using HeroesReplay.CLI.Options;
using HeroesReplay.Core.Analyzer;
using HeroesReplay.Core.Picker;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.CLI.Commands
{
    public class AnalyzerCommand : Command
    {
        public AnalyzerCommand() : base("analyze", "Analyze .StormReplay files to be used for spectating")
        {
            AddOption(new AwsAccessKeyOption(Constants.HEROES_REPLAY_AWS_ACCESS_KEY));
            AddOption(new AwsSecretKeyOption(Constants.HEROES_REPLAY_AWS_SECRET_KEY));
            AddOption(new MinimumReplayIdOption(Constants.REPLAY_ID_UNSET));

            AddOption(new Option("--source", description: "The source for the .StormReplay files") { Argument = new Argument<Uri>() });
            AddOption(new Option("--destination", description: "The destination for the processed .StormReplay files") { Argument = new Argument<Uri>() });

            Handler = CommandHandler.Create<string, string, int, Uri, Uri, CancellationToken>(CommandAsync);
        }

        private async Task CommandAsync(string awsAccessKey, string awsSecretKey, int minReplayId, Uri source, Uri destination, CancellationToken cancellationToken)
        {
            var configuration = new ConfigurationBuilder()
              .SetBasePath(Directory.GetCurrentDirectory())
              .AddJsonFile("appsettings.json")
              .AddInMemoryCollection(new[]
              {
                    new KeyValuePair<string, string>(Constants.ConfigKeys.MinReplayId, minReplayId.ToString()),
                    new KeyValuePair<string, string>(Constants.ConfigKeys.AwsAccessKey, awsAccessKey),
                    new KeyValuePair<string, string>(Constants.ConfigKeys.AwsSecretKey, awsSecretKey),
                    new KeyValuePair<string, string>(Constants.ConfigKeys.ReplaySource, source.OriginalString),
                    new KeyValuePair<string, string>(Constants.ConfigKeys.ReplayDestination, destination.OriginalString)
              })
              .Build();

            ServiceProvider serviceProvider = new ServiceCollection()
                .AddLogging(builder => builder.AddConfiguration(configuration.GetSection("Logging")).AddConsole())
                .AddSingleton<IConfiguration>(provider => configuration)
                .AddSingleton(provider => new CancellationTokenProvider(cancellationToken))
                .AddSingleton<StormReplayAnalyzer>()
                .AddSingleton<StormReplayProcessor>()
                .AddSingleton<StormReplayPicker>()
                .AddSingleton<StormPlayerTool>()
                .AddSingleton(typeof(IStormReplayProvider), source.IsFile ? typeof(StormReplayDirectoryProvider) : typeof(StormReplayHotsApiProvider))
                .AddSingleton(typeof(IStormReplaySaver), destination.IsFile ? typeof(StormReplayDirectorySaver) : typeof(StormReplayDirectorySaver))
                .BuildServiceProvider();

            using (IServiceScope scope = serviceProvider.CreateScope())
            {
                StormReplayProcessor processor = scope.ServiceProvider.GetRequiredService<StormReplayProcessor>();

                await processor.RunAsync();
            }
        }
    }
}