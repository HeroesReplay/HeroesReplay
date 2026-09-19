using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Providers;

public class ReplayLoader : IReplayLoader
{
    private readonly ILogger<ReplayLoader> logger;
    private readonly AppSettings settings;

    private readonly ParseOptions options;

    public ReplayLoader(ILogger<ReplayLoader> logger, AppSettings settings)
    {
        this.logger = logger;
        this.settings = settings;

        options = new()
        {
            AllowPTR = false,
            ShouldParseDetailedBattleLobby = settings.ParseOptions.ShouldParseEvents,
            ShouldParseEvents = settings.ParseOptions.ShouldParseEvents,
            ShouldParseMouseEvents = settings.ParseOptions.ShouldParseMouseEvents,
            ShouldParseStatistics = settings.ParseOptions.ShouldParseStatistics,
            ShouldParseUnits = settings.ParseOptions.ShouldParseUnits,
            ShouldParseMessageEvents = settings.ParseOptions.ShouldParseMessageEvents,
            IgnoreErrors = false,
        };
    }

    public async Task<Replay> LoadAsync(string path)
    {
        using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.replay.parse");
        activity?.SetTag("replay.path", path);
        try
        {
            (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(
                await File.ReadAllBytesAsync(path).ConfigureAwait(false),
                options
            );
            activity?.SetTag("replay.parse_result", result.ToString());
            if (
                result == DataParser.ReplayParseResult.Success
                || result == DataParser.ReplayParseResult.UnexpectedResult
            )
            {
                activity?.SetTag("replay.map", replay?.Map);
                activity?.SetTag("replay.version", replay?.ReplayVersion);
                return replay;
            }

            activity?.SetStatus(ActivityStatusCode.Error, result.ToString());
            logger.LogError($"There was an error parsing the replay: {path}. Result: {result}");
        }
        catch (Exception e)
        {
            HeroesReplayTelemetry.RecordException(activity, e);
            logger.LogError(e, "There was an error parsing the replay: {Path}.", path);
        }

        return null;
    }
}
