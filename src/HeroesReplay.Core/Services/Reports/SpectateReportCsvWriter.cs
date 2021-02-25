using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Analysis;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Providers;
using HeroesReplay.Core.Services.Shared;

namespace HeroesReplay.Core.Services.Reports
{
    public class SpectateReportCsvWriter : ISpectateReportWriter
    {
        private readonly IGameData gameData;
        private readonly AppSettings settings;
        private readonly IReplayProvider provider;
        private readonly IReplayAnalyzer analyzer;
        private readonly ConsoleTokenProvider tokenProvider;

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

        public SpectateReportCsvWriter(IGameData gameData, AppSettings settings, IReplayProvider provider, IReplayAnalyzer analyzer, ConsoleTokenProvider tokenProvider)
        {
            this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        }

        public async Task OutputReportAsync()
        {
            await gameData.LoadDataAsync().ConfigureAwait(false);

            LoadedReplay loadedReplay = await provider.TryLoadNextReplayAsync();

            IEnumerable<string> lines = analyzer.GetPlayers(loadedReplay.Replay)
                                                   .Select(WriteCsvLine)
                                                   .Prepend(string.Join(",", headers));

            Directory.CreateDirectory(settings.SpectateReportPath);

            string report = Path.Combine(settings.SpectateReportPath, Path.GetFileName(loadedReplay.FileInfo.FullName) + ".csv");

            await File.WriteAllLinesAsync(report, lines, tokenProvider.Token);
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
