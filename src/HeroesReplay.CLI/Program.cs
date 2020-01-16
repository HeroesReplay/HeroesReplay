using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.CLI
{
    using Spectator;

    static class Program
    {
        public static async Task Main(string[] args)
        {
            using (var cts = new CancellationTokenSource())
            {
                ServiceProvider serviceProvider = new ServiceCollection()
               .AddLogging(loggingBuilder => loggingBuilder.AddConsole())
               .AddSingleton<IConfiguration>(provider => new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").Build())
               .AddSingleton(provider => new CommandLineBuilder(provider.GetRequiredService<RootReplayCommand>()).UseVersionOption().UseParseErrorReporting().UseHelp().UseVersionOption().Build())
               .AddSingleton(provider => new CancellationTokenProvider(cts.Token))
               .AddSingleton<RootReplayCommand>()
               .AddSingleton<HeroesOfTheStorm>()
               .AddSingleton<BattleNet>()
               .AddSingleton<IStormReplayAnalyzer, StormReplayAnalyzer>()
               .AddSingleton<StormPlayerSelector>()
               .AddSingleton<Spectator>()
               .AddSingleton<StormReplayProvider>()
               .AddSingleton<StormReplayConsumer>()
               .AddSingleton<StormReplayRunner>()
               .AddSingleton<AdminChecker>()
               .BuildServiceProvider(new ServiceProviderOptions() { ValidateOnBuild = true });

                using (IServiceScope scope = serviceProvider.CreateScope())
                {
                    Parser commandLineParser = scope.ServiceProvider.GetRequiredService<Parser>();

                    ParseResult result = commandLineParser.Parse(args);

                    if (result.Errors.Any())
                    {
                        await commandLineParser.InvokeAsync(result);
                    }
                    else
                    {

                        Console.CancelKeyPress += (sender, e) =>
                        {
                            e.Cancel = true;
                            cts.Cancel();
                        };

                        IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                        bool launch = result.ValueForOption<bool>("--launch");
                        string path = result.ValueForOption<string>("--path");
                        string bnet = result.ValueForOption<DirectoryInfo>("--bnet").FullName;

                        configuration["bnet"] = bnet;
                        configuration["path"] = path;
                        configuration["launch"] = launch.ToString();

                        StormReplayConsumer service = scope.ServiceProvider.GetRequiredService<StormReplayConsumer>();

                        await service.ReplayAsync(path, launch);
                    }
                }
            }
        }
    }
}
