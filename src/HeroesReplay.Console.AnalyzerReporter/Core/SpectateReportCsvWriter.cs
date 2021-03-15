using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Analyzer;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Providers;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Console.AnalyzerReporter.Core
{
    public class SpectateReportCsvWriter : ISpectateReportWriter
    {
        private readonly IGameData gameData;
        private readonly IOptions<AppSettings> options;
        private readonly IReplayProvider provider;
        private readonly IReplayAnalyzer analyzer;
        private readonly CancellationTokenSource tokenSource;

        private readonly string[] headers = new[]
        {
            nameof(KeyValuePair<TimeSpan, Focus>.Key),
            nameof(Focus.Calculator),
            nameof(Focus.Unit),
            nameof(Focus.Target),
            nameof(Focus.Index),
            nameof(Focus.Points),
            nameof(Focus.Description)
        };

        public SpectateReportCsvWriter(IGameData gameData, IOptions<AppSettings> options, IReplayProvider provider, IReplayAnalyzer analyzer, CancellationTokenSource tokenSource)
        {
            this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            this.tokenSource = tokenSource ?? throw new ArgumentNullException(nameof(tokenSource));
        }

        public async Task OutputReportAsync()
        {
            await gameData.LoadDataAsync().ConfigureAwait(false);

            LoadedReplay loadedReplay = await provider.TryLoadNextReplayAsync();

            IEnumerable<string> lines = analyzer.GetPlayers(loadedReplay.Replay)
                                                   .Select(WriteCsvLine)
                                                   .Prepend(string.Join(",", headers));

            Directory.CreateDirectory(options.Value.SpectateReportPath);

            string report = Path.Combine(options.Value.SpectateReportPath, Path.GetFileName(loadedReplay.FileInfo.FullName) + ".csv");

            await File.WriteAllLinesAsync(report, lines, tokenSource.Token);
        }

        private static string WriteCsvLine(KeyValuePair<TimeSpan, Focus> result)
        {
            var time = result.Key;
            var calculator = result.Value.Calculator.Name;
            var unit = result.Value.Unit.Name;
            var hero = result.Value.Target.Character;
            var heroIndex = result.Value.Index;
            var weight = result.Value.Points;
            var description = result.Value.Description;

            return $"{time},{calculator},{unit},{hero},{heroIndex},{weight},{description}";
        }
    }
}
