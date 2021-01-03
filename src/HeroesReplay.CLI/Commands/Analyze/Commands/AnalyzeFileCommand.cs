using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Providers;
using HeroesReplay.Core.Runner;

using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands
{
    public class AnalyzeFileCommand : Command
    {
        public AnalyzeFileCommand() : base("file", "The individual .StormReplay file to analyze.")
        {
            Handler = CommandHandler.Create<CancellationToken>(CommandAsync);
        }

        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (IServiceScope scope = new ServiceCollection().AddCoreServices(cancellationToken, typeof(ReplayFileProvider)).BuildServiceProvider().CreateScope())
            {
                IGameData gameData = scope.ServiceProvider.GetRequiredService<IGameData>();
                AppSettings settings = scope.ServiceProvider.GetRequiredService<AppSettings>();
                await gameData.LoadDataAsync().ConfigureAwait(false);

                IReplayProvider provider = scope.ServiceProvider.GetRequiredService<IReplayProvider>();
                IReplayAnalzer analyzer = scope.ServiceProvider.GetRequiredService<IReplayAnalzer>();

                StormReplay stormReplay = await provider.TryLoadReplayAsync();

                string header = "Time,Calculator,Unit,Target,Index,Points,Description";
                IEnumerable<string> csvLines = analyzer.GetPlayers(stormReplay.Replay)
                    .Select(result => $"{result.Key},{result.Value.Calculator.Name},{result.Value.Unit.Name},{result.Value.Target.Character},{result.Value.Index},{result.Value.Points},{result.Value.Description}")
                    .Prepend(header);

                Directory.CreateDirectory(settings.AnalyzePath);

                await File.WriteAllLinesAsync(Path.Combine(settings.AnalyzePath, Path.GetFileName(stormReplay.Path) + ".csv"), csvLines, cancellationToken);
            }
        }
    }
}